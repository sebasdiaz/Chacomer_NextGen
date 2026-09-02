using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Hosting;
using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse por SDK. Esta app no habla con F&O ni con ningun satelite: solo escribe leads.
builder.Services.AddEipDataverse(builder.Configuration);

// Una sola conexion a Dataverse por proceso, igual que AxxonCustomers: ServiceClient es
// thread-safe y reconectar en cada mensaje solo agrega latencia.
builder.Services.AddSingleton<IOrganizationService>(sp =>
    sp.GetRequiredService<DataverseClientFactory>().CreateOrganizationService());

// Fail fast: sin el nombre de la cola el trigger no resuelve, la funcion queda "in error"
// y el host levanta con "No job functions found" — la app corre pero no consume nada, y no
// falla en ningun lado visible. Mismo criterio que AxxonCustomers con su cola de LTM.
var intakeQueueName = builder.Configuration["LeadIntakeServiceBusQueueName"];

if (string.IsNullOrWhiteSpace(intakeQueueName))
    throw new InvalidOperationException(
        "La variable de entorno 'LeadIntakeServiceBusQueueName' no esta configurada.");

// Las dos columnas de 'lead' que dependen del org. Ver LeadIntakeOptions.
builder.Services.AddSingleton(new LeadIntakeOptions
{
    IdentificationAttribute =
        builder.Configuration["LeadIdentificationAttribute"] is { } identification &&
        !string.IsNullOrWhiteSpace(identification)
            ? identification
            : LeadIntakeOptions.DefaultIdentificationAttribute,

    ExternalIdAttribute = builder.Configuration["LeadExternalIdAttribute"]
});

builder.Services.AddTransient<LeadEntityBuilder>();
builder.Services.AddTransient<ILeadIntakeService, LeadIntakeService>();

builder.Build().Run();
