using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
using Axxon.Eip.Core.Messaging;
using AxxonCustomers.Functions.Configuration;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse + cliente OData de F&O (con retry 429/Retry-After).
builder.Services.AddEipDataverse(builder.Configuration);
builder.Services.AddEipFoOData(builder.Configuration);

// Una sola conexion a Dataverse por proceso: ServiceClient es thread-safe y
// reconectar en cada mensaje solo agrega latencia.
builder.Services.AddSingleton<IOrganizationService>(sp =>
    sp.GetRequiredService<DataverseClientFactory>().CreateOrganizationService());

var settings = new AppSettings
{
    // Valor que QualifyLead escribe en msdyn_sellable del contact antes de sincronizar.
    // Ausente o no booleano = no se sella nada (comportamiento historico).
    QualifyLeadSellableValue =
        AppSettings.ParseSellableValue(builder.Configuration["QualifyLeadSellableValue"])
};
builder.Services.AddSingleton(settings);

// Mapeos por JSON (export de Dual Write + overlay), compilados al arranque.
builder.Services.AddSingleton(sp => EntityMapRegistry.Load(
    EntityMapRegistry.DefaultDirectory,
    sp.GetRequiredService<ILogger<EntityMapRegistry>>()));

builder.Services.AddSingleton<FoSchemaCache>();
builder.Services.AddTransient<IFoSchemaProvider, FoSchemaProvider>();
builder.Services.AddTransient<FoPayloadBuilder>();

// Mapeo hacia LTMCustTable. Va en C# y no en un overlay JSON porque navega cadenas de dos
// saltos, consulta con filtro y sale a una relacion 1:N — nada de eso entra en las cinco
// primitivas del motor declarativo (ADR-001).
//
// El cache de catalogos es singleton: sus dos entradas salen de virtual entities, y cada
// Retrieve sobre una virtual entity es Dataverse llamando en vivo a F&O.
builder.Services.AddSingleton<LtmCatalogCache>();
builder.Services.AddTransient<LtmCatalogResolver>();
builder.Services.AddTransient<LtmCustPayloadBuilder>();

// Estado de las legal entities respecto de Dual Write (cdm_isenabledfordualwrite).
// El cache es singleton; el resolver, transient (depende de IOrganizationService).
builder.Services.AddSingleton<DualWriteCompanyCache>();
builder.Services.AddTransient<IDualWriteCompanyResolver, DualWriteCompanyResolver>();

builder.Services.AddTransient<IFoCustomerService, FoCustomerService>();
builder.Services.AddTransient<ICustomerSyncService, CustomerSyncService>();
builder.Services.AddTransient<ISellableStamper, SellableStamper>();
builder.Services.AddTransient<ILtmCustService, LtmCustService>();
builder.Services.AddTransient<ILtmCustSyncService, LtmCustSyncService>();
builder.Services.AddTransient<ILtmCustBackfillService, LtmCustBackfillService>();

// Cola de LTMCustTable. Se valida al arranque igual que en AxxonContacts: sin el nombre de
// la cola, CustomerSyncService no podria encolar la contraparte de localizacion y el alta
// quedaria a medias sin que nadie se entere.
var ltmSyncQueueName = builder.Configuration["LtmSyncServiceBusQueueName"];

if (string.IsNullOrWhiteSpace(ltmSyncQueueName))
    throw new InvalidOperationException(
        "La variable de entorno 'LtmSyncServiceBusQueueName' no esta configurada.");

builder.Services.AddEipServiceBusPublisher(builder.Configuration);
builder.Services.AddTransient(sp => new LtmSyncDispatcher(
    sp.GetRequiredService<IEipMessagePublisher>(),
    ltmSyncQueueName,
    sp.GetRequiredService<ILogger<LtmSyncDispatcher>>()));

var app = builder.Build();

// Fail fast: un mapeo que no compila voltea el host aca y no en el primer mensaje.
// Un mapeo mal escrito no falla solo, escribe mal en F&O y nadie se entera.
app.Services.GetRequiredService<EntityMapRegistry>();

app.Run();
