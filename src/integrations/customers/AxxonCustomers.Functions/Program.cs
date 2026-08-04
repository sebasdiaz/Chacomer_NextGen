using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
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

// Mapeos por JSON (export de Dual Write + overlay), compilados al arranque.
builder.Services.AddSingleton(sp => EntityMapRegistry.Load(
    EntityMapRegistry.DefaultDirectory,
    sp.GetRequiredService<ILogger<EntityMapRegistry>>()));

builder.Services.AddSingleton<FoSchemaCache>();
builder.Services.AddTransient<IFoSchemaProvider, FoSchemaProvider>();
builder.Services.AddTransient<FoPayloadBuilder>();

// Estado de las legal entities respecto de Dual Write (cdm_isenabledfordualwrite).
// El cache es singleton; el resolver, transient (depende de IOrganizationService).
builder.Services.AddSingleton<DualWriteCompanyCache>();
builder.Services.AddTransient<IDualWriteCompanyResolver, DualWriteCompanyResolver>();

builder.Services.AddTransient<IFoCustomerService, FoCustomerService>();
builder.Services.AddTransient<ICustomerSyncService, CustomerSyncService>();

var app = builder.Build();

// Fail fast: un mapeo que no compila voltea el host aca y no en el primer mensaje.
// Un mapeo mal escrito no falla solo, escribe mal en F&O y nadie se entera.
app.Services.GetRequiredService<EntityMapRegistry>();

app.Run();
