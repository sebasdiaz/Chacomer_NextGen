using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
using AxxonCustomers.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse + cliente OData de F&O (con retry 429/Retry-After).
builder.Services.AddEipDataverse(builder.Configuration);
builder.Services.AddEipFoOData(builder.Configuration);

builder.Services.AddTransient<IFoCustomerService, FoCustomerService>();

builder.Services.AddTransient<IContactCustomerSyncService>(sp =>
{
    var factory           = sp.GetRequiredService<DataverseClientFactory>();
    var orgService        = factory.CreateOrganizationService();
    var foCustomerService = sp.GetRequiredService<IFoCustomerService>();
    var logger            = sp.GetRequiredService<ILogger<ContactCustomerSyncService>>();
    return new ContactCustomerSyncService(orgService, foCustomerService, logger);
});

builder.Build().Run();
