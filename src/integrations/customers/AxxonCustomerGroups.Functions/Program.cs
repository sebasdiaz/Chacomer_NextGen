using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
using AxxonCustomerGroups.Functions.Configuration;
using AxxonCustomerGroups.Functions.Services;
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

var settings = new AppSettings
{
    // Legal entities que ya sincroniza Dual Write: se excluyen del sync.
    // Formato: dataAreaIds separados por coma (ej: "cha,cne").
    DualWriteLegalEntities = (builder.Configuration["DualWriteLegalEntities"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
};
builder.Services.AddSingleton(settings);

builder.Services.AddTransient<IFoCustomerGroupService, FoCustomerGroupService>();

builder.Services.AddTransient<ICustomerGroupSyncService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<CustomerGroupSyncService>>();
    return new CustomerGroupSyncService(orgService, logger);
});

builder.Build().Run();
