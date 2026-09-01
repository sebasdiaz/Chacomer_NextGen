using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Hosting;
using AxxonCustomerData.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse por Managed Identity. Requiere el app setting DataverseUrl y la MI de la app
// dada de alta como Application User — ver docs/wiki/integraciones/customerdata.md.
builder.Services.AddEipDataverse(builder.Configuration);

// Singleton, con el mismo criterio que AxxonFiscal: los triggers son HTTP y varias
// invocaciones corren en paralelo dentro de la misma instancia. Un ServiceClient por
// request pagaria el handshake de auth en cada llamada, y esta app escala a 40 instancias.
// ServiceClient soporta llamadas concurrentes, y el lookup es de solo lectura y sin estado.
// El cliente se crea recien en la primera resolucion, no al levantar el host.
builder.Services.AddSingleton<ClienteLookupService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<ClienteLookupService>>();
    return new ClienteLookupService(orgService, logger);
});

builder.Build().Run();
