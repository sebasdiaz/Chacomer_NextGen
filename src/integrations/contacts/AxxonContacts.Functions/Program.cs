using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Fiscal;
using Axxon.Eip.Core.Hosting;
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

// Cliente de la SET (DNIT) para validar RUC durante el matching. API Key: config "SetApiKey".
// Los endpoints HTTP fiscales (consulta RUC / Turuc / validez documento) viven en
// AxxonFiscal.Functions; aca solo se consume SetApiService para el path de mensajeria.
builder.Services.AddEipSetApi(builder.Configuration);

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

builder.Build().Run();
