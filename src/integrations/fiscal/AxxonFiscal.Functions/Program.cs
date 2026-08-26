using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Fiscal;
using Axxon.Eip.Core.Hosting;
using AxxonFiscal.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Clientes de consultas fiscales (proxies HTTP puros — sin Dataverse ni Service Bus).
//   SET (DNIT): API Key desde config "SetApiKey" (Key Vault secret).
//   TURUC: API publica, sin credenciales.
builder.Services.AddEipSetApi(builder.Configuration);
builder.Services.AddEipTuruc();

// Dataverse via Managed Identity: lo necesita solo la consulta de partes por RUC
// (Dataverse_ConsultaRuc). Requiere el app setting DataverseUrl y la MI de la app dada
// de alta como Application User — ver docs/wiki/integraciones/fiscal.md.
builder.Services.AddEipDataverse(builder.Configuration);

// Singleton, a diferencia del Transient de AxxonContacts: aca los triggers son HTTP y
// varias invocaciones corren en paralelo dentro de la misma instancia. Un ServiceClient
// por request pagaria el handshake de auth en cada llamada, y esta app escala a 40
// instancias. ServiceClient soporta llamadas concurrentes, y el lookup es de solo
// lectura y sin estado. El cliente se crea recien en la primera resolucion, no al
// levantar el host.
builder.Services.AddSingleton<PartyLookupService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<PartyLookupService>>();
    return new PartyLookupService(orgService, logger);
});

builder.Build().Run();
