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
    /// FoClientSecret (solo DESA/INTE — en Key Vault). Si el secret del vault se llama
    /// distinto, indicarlo en el app setting "FoClientSecretName"
    /// (ver <see cref="EipSecretResolver"/>).
    /// </summary>
    public static class EipFoODataExtensions
    {
        public static IServiceCollection AddEipFoOData(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = new FoODataOptions
            {
                BaseUrl      = configuration["FoBaseUrl"] ?? string.Empty,
                TenantId     = configuration["FoTenantId"],
                ClientId     = configuration["FoClientId"],
                ClientSecret = configuration.ResolveSecret("FoClientSecret")
            };

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new InvalidOperationException(
                    "La variable de entorno 'FoBaseUrl' no esta configurada.");

            services.AddSingleton(options);

            services.AddHttpClient(FoODataClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
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
                    MaxRetryAttempts = 3,
                    Delay            = TimeSpan.FromSeconds(5),
                    BackoffType      = DelayBackoffType.Exponential,
                    UseJitter        = true,
                    ShouldHandle     = args => ValueTask.FromResult(
                        args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }),
                    // Respeta el Retry-After que manda F&O, con tope de 60s para no
                    // exceder el Timeout del HttpClient (5 min) ni el lock del
                    // mensaje de Service Bus. Sin header, cae al backoff exponencial.
                    DelayGenerator = args =>
                    {
                        var retryAfter  = args.Outcome.Result?.Headers.RetryAfter;
                        TimeSpan? delay = retryAfter?.Delta
                            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

                        if (delay is { Ticks: > 0 } d)
                            return ValueTask.FromResult<TimeSpan?>(
                                d <= TimeSpan.FromSeconds(60) ? d : TimeSpan.FromSeconds(60));

                        return ValueTask.FromResult<TimeSpan?>(null);
                    },
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "[FoOData] HTTP 429 (throttling) de F&O. " +
                            "Reintento {Attempt}/{Max} en {Delay}s.",
                            args.AttemptNumber + 1, 3, args.RetryDelay.TotalSeconds);
                        return ValueTask.CompletedTask;
                    }
                });
            });

            return builder;
        }
    }
}
