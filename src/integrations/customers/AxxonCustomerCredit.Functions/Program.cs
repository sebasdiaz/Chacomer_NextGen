using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
using AxxonCustomerCredit.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Cliente OData de F&O (Managed Identity, cross-company y retry de 429/Retry-After).
// Requiere el app setting FoBaseUrl — sin el, la app no arranca.
// No se registra Dataverse: esta app no lo toca.
builder.Services.AddEipFoOData(builder.Configuration);

// Transient como el resto de los servicios que solo envuelven al IFoODataClient:
// no tiene estado y el cliente OData ya se registra transient sobre un HttpClient
// del factory.
builder.Services.AddTransient<IFoCreditoService, FoCreditoService>();

builder.Build().Run();
