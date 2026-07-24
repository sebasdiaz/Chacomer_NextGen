using Axxon.Eip.Core.Fiscal;
using Axxon.Eip.Core.Hosting;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Clientes de consultas fiscales (proxies HTTP puros — sin Dataverse ni Service Bus).
//   SET (DNIT): API Key desde config "SetApiKey" (Key Vault secret).
//   TURUC: API publica, sin credenciales.
builder.Services.AddEipSetApi(builder.Configuration);
builder.Services.AddEipTuruc();

builder.Build().Run();
