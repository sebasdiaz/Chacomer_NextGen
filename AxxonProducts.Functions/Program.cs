using Azure.Monitor.OpenTelemetry.Exporter;
using AxxonProducts.Functions.Configuration;
using AxxonProducts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .UseAzureMonitorExporter();

var settings = new AppSettings
{
    DataverseUrl          = builder.Configuration["DataverseUrl"] ?? string.Empty,
    DataverseClientId     = builder.Configuration["DataverseClientId"],
    DataverseClientSecret = builder.Configuration["DataverseClientSecret"],
    FoBaseUrl             = builder.Configuration["FoBaseUrl"] ?? string.Empty,
    FoTenantId            = builder.Configuration["FoTenantId"],
    FoClientId            = builder.Configuration["FoClientId"],
    FoClientSecret        = builder.Configuration["FoClientSecret"]
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

builder.Services.AddLogging(b => b.AddConsole());

builder.Build().Run();
