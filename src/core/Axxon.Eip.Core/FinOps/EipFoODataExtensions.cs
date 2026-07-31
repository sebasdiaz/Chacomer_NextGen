using System.Net;
using Axxon.Eip.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Registra el cliente OData de F&amp;O en el contenedor de DI, con el
    /// HttpClient nombrado "FoOData" (headers OData + retry de throttling).
    /// Claves de configuracion: FoBaseUrl (obligatoria), FoTenantId, FoClientId,
    /// FoClientSecret (solo DESA — en Key Vault como secret "FoClientSecret").
    /// </summary>
    public static class EipFoODataExtensions
    {
        /// <summary>Reintentos ante 429 antes de propagar el error.</summary>
        private const int MaxThrottlingRetries = 5;

        /// <summary>Tope al Retry-After de F&amp;O, para que una sola espera no agote el Timeout.</summary>
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(3);


        public static IServiceCollection AddEipFoOData(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = new FoODataOptions
            {
                BaseUrl      = configuration["FoBaseUrl"] ?? string.Empty,
                TenantId     = configuration["FoTenantId"],
                ClientId     = configuration["FoClientId"],
                ClientSecret = configuration["FoClientSecret"]
            };

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new InvalidOperationException(
                    "La variable de entorno 'FoBaseUrl' no esta configurada.");

            services.AddSingleton(options);

            services.AddHttpClient(FoODataClient.HttpClientName, client =>
            {
                // El Timeout cubre TODA la operacion, reintentos del resilience handler
                // incluidos (HttpClient arma el CTS antes de entrar a la cadena de
                // handlers). Tiene que ser mayor que la ventana de backoff de
                // AddFoThrottlingRetry (~5 min) y menor que el maxAutoLockRenewalDuration
                // de los triggers de Service Bus (10 min en host.json).
                client.Timeout = TimeSpan.FromMinutes(8);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            })
            .AddFoThrottlingRetry();

            services.AddTransient<IFoODataClient>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(FoODataClient.HttpClientName);
                return new FoODataClient(
                    httpClient,
                    sp.GetRequiredService<FoODataOptions>(),
                    sp.GetRequiredService<ILogger<FoODataClient>>());
            });

            return services;
        }

        /// <summary>
        /// Retry ante service protection de F&amp;O.
        ///
        /// Reintenta SOLO HTTP 429: el server rechaza la request sin procesarla,
        /// asi que el reintento es seguro incluso para POST no idempotentes.
        /// 5xx/timeouts NO se reintentan aca para no duplicar inserts: de esos se
        /// encarga el reintento de Service Bus o la proxima corrida del Timer.
        ///
        /// F&amp;O tiene dos familias de 429 y el backoff esta dimensionado para la peor:
        ///
        ///   - Priority-based ("you have exceeded your allotted quota"): es por usuario
        ///     autenticado y viene con Retry-After. Se respeta ese header.
        ///   - Resource-based ("system experiencing high resource utilization"): el
        ///     entorno esta saturado, aplica a todos los usuarios y normalmente llega
        ///     SIN Retry-After. Dura minutos, no segundos, asi que el fallback tiene que
        ///     arrancar en decenas de segundos: con el backoff viejo (5/10/20s) la
        ///     ventana total era de ~35s y el fallo estaba garantizado.
        ///
        /// Ventana nominal actual: 10+20+40+80+160 = ~5 min, por debajo del Timeout del
        /// HttpClient (8 min) y del maxAutoLockRenewalDuration de Service Bus (10 min),
        /// asi que la espera se absorbe dentro de una sola entrega del mensaje en vez de
        /// consumir delivery count.
        /// </summary>
        public static IHttpClientBuilder AddFoThrottlingRetry(this IHttpClientBuilder builder)
        {
            builder.AddResilienceHandler("FoThrottlingRetry", (resilience, context) =>
            {
                var logger = context.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FoThrottlingRetry");

                resilience.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = MaxThrottlingRetries,
                    Delay            = TimeSpan.FromSeconds(10),
                    BackoffType      = DelayBackoffType.Exponential,
                    UseJitter        = true,
                    ShouldHandle     = args => ValueTask.FromResult(
                        args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }),
                    // Respeta el Retry-After que manda F&O, con tope de 3 min para no
                    // agotar el Timeout del HttpClient de una sola espera. Sin header
                    // (tipico de la throttling por saturacion de recursos) cae al
                    // backoff exponencial.
                    DelayGenerator = args =>
                    {
                        var retryAfter  = args.Outcome.Result?.Headers.RetryAfter;
                        TimeSpan? delay = retryAfter?.Delta
                            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

                        if (delay is { Ticks: > 0 } d)
                            return ValueTask.FromResult<TimeSpan?>(d <= MaxRetryAfter ? d : MaxRetryAfter);

                        return ValueTask.FromResult<TimeSpan?>(null);
                    },
                    OnRetry = args =>
                    {
                        // Distinguir las dos familias de throttling en el log: si no hay
                        // Retry-After es casi seguro saturacion del entorno, que no se
                        // arregla bajando el ritmo de esta app.
                        var hasRetryAfter = args.Outcome.Result?.Headers.RetryAfter != null;

                        logger.LogWarning(
                            "[FoOData] HTTP 429 (throttling) de F&O. Reintento {Attempt}/{Max} " +
                            "en {Delay}s. Retry-After presente={HasRetryAfter}.",
                            args.AttemptNumber + 1, MaxThrottlingRetries,
                            args.RetryDelay.TotalSeconds, hasRetryAfter);
                        return ValueTask.CompletedTask;
                    }
                });
            });

            return builder;
        }
    }
}
