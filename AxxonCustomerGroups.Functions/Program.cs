using System.Net;
using AxxonCustomerGroups.Functions.Configuration;
using AxxonCustomerGroups.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var settings = new AppSettings
        {
            DataverseUrl          = context.Configuration["DataverseUrl"] ?? string.Empty,
            DataverseClientId     = context.Configuration["DataverseClientId"],
            DataverseClientSecret = context.Configuration["DataverseClientSecret"],
            FoBaseUrl             = context.Configuration["FoBaseUrl"] ?? string.Empty,
            FoTenantId            = context.Configuration["FoTenantId"],
            FoClientId            = context.Configuration["FoClientId"],
            FoClientSecret        = context.Configuration["FoClientSecret"],

            // Legal entities que ya sincroniza Dual Write: se excluyen del sync.
            // Formato: dataAreaIds separados por coma (ej: "cha,cne").
            DualWriteLegalEntities = (context.Configuration["DualWriteLegalEntities"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };

        if (string.IsNullOrWhiteSpace(settings.DataverseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'DataverseUrl' no esta configurada.");

        if (string.IsNullOrWhiteSpace(settings.FoBaseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'FoBaseUrl' no esta configurada.");

        services.AddSingleton(settings);

        // DataverseClientFactory Transient: cada invocacion obtiene su propio ServiceClient.
        services.AddTransient<DataverseClientFactory>();

        services.AddHttpClient("FoOData", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        })
        .AddResilienceHandler("FoThrottlingRetry", (resilience, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FoThrottlingRetry");

            // Reintenta SOLO HTTP 429 (service protection de F&O): el server
            // rechaza la request sin procesarla, asi que el reintento es seguro.
            // Otros errores se propagan para que el Timer los reporte y el sync
            // se reintente completo en la proxima corrida.
            resilience.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(5),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                ShouldHandle     = args => ValueTask.FromResult(
                    args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }),
                // Respeta el Retry-After que manda F&O, con tope de 60s para no
                // exceder el Timeout del HttpClient (5 min). Sin header, cae al
                // backoff exponencial.
                DelayGenerator = args =>
                {
                    var retryAfter   = args.Outcome.Result?.Headers.RetryAfter;
                    TimeSpan? delay  = retryAfter?.Delta
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

        services.AddTransient<IFoCustomerGroupService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("FoOData");
            var appSettings       = sp.GetRequiredService<AppSettings>();
            var logger            = sp.GetRequiredService<ILogger<FoCustomerGroupService>>();
            return new FoCustomerGroupService(httpClient, appSettings, logger);
        });

        services.AddTransient<ICustomerGroupSyncService>(sp =>
        {
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<CustomerGroupSyncService>>();
            return new CustomerGroupSyncService(orgService, logger);
        });

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

await host.RunAsync();
