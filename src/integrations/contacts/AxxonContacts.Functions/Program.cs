using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Hosting;
using AxxonContacts.Functions.Configuration;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse via Managed Identity (o Client Secret en DESA).
// DataverseClientFactory Transient: cada invocacion obtiene su propio ServiceClient.
// Sessions de Service Bus garantizan maxConcurrentCallsPerSession=1 por cliente,
// por lo que un ServiceClient por invocacion es seguro sin estado compartido.
builder.Services.AddEipDataverse(builder.Configuration);

var settings = new AppSettings
{
    SetApiKey = builder.Configuration["SetApiKey"]
};
builder.Services.AddSingleton(settings);

builder.Services.AddTransient<MasterMatchingService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<MasterMatchingService>>();
    return new MasterMatchingService(orgService, logger);
});

builder.Services.AddTransient<AccountMasterMatchingService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<AccountMasterMatchingService>>();
    return new AccountMasterMatchingService(orgService, logger);
});

// HttpClient para la API de TURUC (RUC validation)
builder.Services.AddHttpClient("RucApi", client =>
{
    client.BaseAddress = new Uri("https://turuc.com.py/api/contribuyente/");
    client.Timeout     = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddTransient<RucValidationService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient        = httpClientFactory.CreateClient("RucApi");
    var factory           = sp.GetRequiredService<DataverseClientFactory>();
    var orgService        = factory.CreateOrganizationService();
    var logger            = sp.GetRequiredService<ILogger<RucValidationService>>();
    return new RucValidationService(httpClient, orgService, logger);
});

builder.Services.AddTransient<SetRucValidationService>(sp =>
{
    var setApi     = sp.GetRequiredService<SetApiService>();
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<SetRucValidationService>>();
    return new SetRucValidationService(setApi, orgService, logger);
});

builder.Services.AddTransient<ContactProcessingService>(sp =>
{
    var masterMatchingService   = sp.GetRequiredService<MasterMatchingService>();
    var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
    var logger                  = sp.GetRequiredService<ILogger<ContactProcessingService>>();
    return new ContactProcessingService(masterMatchingService, setRucValidationService, logger);
});

builder.Services.AddTransient<AccountProcessingService>(sp =>
{
    var matchingService         = sp.GetRequiredService<AccountMasterMatchingService>();
    var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
    var logger                  = sp.GetRequiredService<ILogger<AccountProcessingService>>();
    return new AccountProcessingService(matchingService, setRucValidationService, logger);
});

// TurucApiService: proxy HTTP a la API publica de TURUC.
// Reutiliza el mismo HttpClient "RucApi" (mismo base address).
builder.Services.AddTransient<TurucApiService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient        = httpClientFactory.CreateClient("RucApi");
    var logger            = sp.GetRequiredService<ILogger<TurucApiService>>();
    return new TurucApiService(httpClient, logger);
});

// HttpClient para la API oficial de la SET (Subsecretaria de Estado de Tributacion).
builder.Services.AddHttpClient("SetApi", client =>
{
    client.BaseAddress = new Uri("https://servicios.set.gov.py/EsetApiWS/ApiWS/");
    client.Timeout     = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// SetApiService: proxy HTTP a la API oficial de la SET Paraguay.
builder.Services.AddTransient<SetApiService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient        = httpClientFactory.CreateClient("SetApi");
    var appSettings       = sp.GetRequiredService<AppSettings>();
    var logger            = sp.GetRequiredService<ILogger<SetApiService>>();
    return new SetApiService(httpClient, appSettings, logger);
});

builder.Build().Run();
