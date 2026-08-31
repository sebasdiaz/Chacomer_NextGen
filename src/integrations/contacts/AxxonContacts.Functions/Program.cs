using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Fiscal;
using Axxon.Eip.Core.Hosting;
using Axxon.Eip.Core.Messaging;
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

// Equipo dueño de los masters: el "cliente unico" queda en la business unit de ese equipo.
// El setting lleva el nombre del equipo, no su id, porque el GUID cambia por environment
// y el nombre no. Vacio o ausente = los masters se crean con el owner por defecto, que es
// como venia funcionando. Ver MasterOwnerTeamResolver.
var masterOwnerTeamName = builder.Configuration["MasterOwnerTeamName"];

builder.Services.AddSingleton<MasterOwnerTeamCache>();

builder.Services.AddTransient<MasterOwnerTeamResolver>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var cache      = sp.GetRequiredService<MasterOwnerTeamCache>();
    var logger     = sp.GetRequiredService<ILogger<MasterOwnerTeamResolver>>();
    return new MasterOwnerTeamResolver(orgService, cache, masterOwnerTeamName, logger);
});

builder.Services.AddTransient<MasterMatchingService>(sp =>
{
    var factory       = sp.GetRequiredService<DataverseClientFactory>();
    var orgService    = factory.CreateOrganizationService();
    var ownerResolver = sp.GetRequiredService<MasterOwnerTeamResolver>();
    var logger        = sp.GetRequiredService<ILogger<MasterMatchingService>>();
    return new MasterMatchingService(orgService, ownerResolver, logger);
});

builder.Services.AddTransient<AccountMasterMatchingService>(sp =>
{
    var factory       = sp.GetRequiredService<DataverseClientFactory>();
    var orgService    = factory.CreateOrganizationService();
    var ownerResolver = sp.GetRequiredService<MasterOwnerTeamResolver>();
    var logger        = sp.GetRequiredService<ILogger<AccountMasterMatchingService>>();
    return new AccountMasterMatchingService(orgService, ownerResolver, logger);
});

builder.Services.AddTransient<SetRucValidationService>(sp =>
{
    var setApi     = sp.GetRequiredService<SetApiService>();
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<SetRucValidationService>>();
    return new SetRucValidationService(setApi, orgService, logger);
});

// Sincronizacion hacia F&O de las legal entities que Dual Write no cubre: se resuelve
// cdm_isenabledfordualwrite y, si corresponde, se publica en la cola de fo-sync (que
// consume AxxonCustomers.Functions, donde vive el motor de mapeo).
builder.Services.AddEipServiceBusPublisher(builder.Configuration);
builder.Services.AddSingleton<DualWriteCompanyCache>();

builder.Services.AddTransient<IDualWriteCompanyResolver>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var cache      = sp.GetRequiredService<DualWriteCompanyCache>();
    var logger     = sp.GetRequiredService<ILogger<DualWriteCompanyResolver>>();
    return new DualWriteCompanyResolver(orgService, cache, logger);
});

// Fail fast: si falta el nombre de la cola, el host no levanta. Resolverlo recien al
// procesar un mensaje dejaria caido tambien el master matching, que no tiene nada que ver.
var foSyncQueueName = builder.Configuration["FoSyncServiceBusQueueName"];

if (string.IsNullOrWhiteSpace(foSyncQueueName))
    throw new InvalidOperationException(
        "La variable de entorno 'FoSyncServiceBusQueueName' no esta configurada.");

builder.Services.AddTransient<FoSyncDispatcher>(sp =>
{
    var queueName = foSyncQueueName;
    var publisher = sp.GetRequiredService<IEipMessagePublisher>();
    var resolver  = sp.GetRequiredService<IDualWriteCompanyResolver>();
    var logger    = sp.GetRequiredService<ILogger<FoSyncDispatcher>>();
    return new FoSyncDispatcher(publisher, resolver, queueName, logger);
});

builder.Services.AddTransient<ContactProcessingService>(sp =>
{
    var masterMatchingService   = sp.GetRequiredService<MasterMatchingService>();
    var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
    var foSyncDispatcher        = sp.GetRequiredService<FoSyncDispatcher>();
    var logger                  = sp.GetRequiredService<ILogger<ContactProcessingService>>();
    return new ContactProcessingService(
        masterMatchingService, setRucValidationService, foSyncDispatcher, logger);
});

builder.Services.AddTransient<AccountProcessingService>(sp =>
{
    var matchingService         = sp.GetRequiredService<AccountMasterMatchingService>();
    var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
    var foSyncDispatcher        = sp.GetRequiredService<FoSyncDispatcher>();
    var logger                  = sp.GetRequiredService<ILogger<AccountProcessingService>>();
    return new AccountProcessingService(
        matchingService, setRucValidationService, foSyncDispatcher, logger);
});

builder.Build().Run();
