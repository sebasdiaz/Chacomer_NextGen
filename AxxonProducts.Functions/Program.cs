using System.Net;
using Azure.Monitor.OpenTelemetry.Exporter;
using AxxonProducts.Functions.Configuration;
using AxxonProducts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Polly;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var otelBuilder = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults();

var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    otelBuilder.UseAzureMonitorExporter();

var settings = new AppSettings
{
    DataverseUrl             = builder.Configuration["DataverseUrl"] ?? string.Empty,
    DataverseClientId        = builder.Configuration["DataverseClientId"],
    DataverseClientSecret    = builder.Configuration["DataverseClientSecret"],
    FoBaseUrl                = builder.Configuration["FoBaseUrl"] ?? string.Empty,
    FoTenantId               = builder.Configuration["FoTenantId"],
    FoClientId               = builder.Configuration["FoClientId"],
    FoClientSecret           = builder.Configuration["FoClientSecret"],
    AssignOwningBusinessUnit = bool.TryParse(
        builder.Configuration["AssignOwningBusinessUnit"], out var assignBu) && assignBu
};

if (string.IsNullOrWhiteSpace(settings.DataverseUrl))
    throw new InvalidOperationException(
        "La variable de entorno 'DataverseUrl' no esta configurada.");

if (string.IsNullOrWhiteSpace(settings.FoBaseUrl))
    throw new InvalidOperationException(
        "La variable de entorno 'FoBaseUrl' no esta configurada.");

builder.Services.AddSingleton(settings);
builder.Services.AddTransient<DataverseClientFactory>();

builder.Services.AddHttpClient("FoOData", client =>
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

    // Reintenta SOLO HTTP 429 (service protection de F&O): el server rechaza
    // la request sin procesarla, asi que el reintento es seguro. Otros errores
    // se propagan para que el sync los reporte y reintente completo despues.
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

builder.Services.AddTransient<IFoDataService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient        = httpClientFactory.CreateClient("FoOData");
    var appSettings       = sp.GetRequiredService<AppSettings>();
    var logger            = sp.GetRequiredService<ILogger<FoDataService>>();
    return new FoDataService(httpClient, appSettings, logger);
});

builder.Services.AddTransient<ISharedProductSyncService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<SharedProductSyncService>>();
    return new SharedProductSyncService(orgService, logger);
});

builder.Services.AddTransient<IFoProductGroupService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient        = httpClientFactory.CreateClient("FoOData");
    var appSettings       = sp.GetRequiredService<AppSettings>();
    var logger            = sp.GetRequiredService<ILogger<FoProductGroupService>>();
    return new FoProductGroupService(httpClient, appSettings, logger);
});

builder.Services.AddTransient<IProductGroupSyncService>(sp =>
{
    var factory     = sp.GetRequiredService<DataverseClientFactory>();
    var orgService  = factory.CreateOrganizationService();
    var appSettings = sp.GetRequiredService<AppSettings>();
    var logger      = sp.GetRequiredService<ILogger<ProductGroupSyncService>>();
    return new ProductGroupSyncService(orgService, appSettings, logger);
});

builder.Services.AddLogging(b => b.AddConsole());

builder.Build().Run();
