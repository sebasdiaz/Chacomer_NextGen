using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Hosting;
using AxxonProducts.Functions.Configuration;
using AxxonProducts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse + cliente OData de F&O (con retry 429/Retry-After).
builder.Services.AddEipDataverse(builder.Configuration);
builder.Services.AddEipFoOData(builder.Configuration);

var settings = new AppSettings
{
    AssignOwningBusinessUnit = bool.TryParse(
        builder.Configuration["AssignOwningBusinessUnit"], out var assignBu) && assignBu
};
builder.Services.AddSingleton(settings);

builder.Services.AddTransient<IFoDataService, FoDataService>();
builder.Services.AddTransient<IFoProductGroupService, FoProductGroupService>();

builder.Services.AddTransient<ISharedProductSyncService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<SharedProductSyncService>>();
    return new SharedProductSyncService(orgService, logger);
});

builder.Services.AddTransient<IProductGroupSyncService>(sp =>
{
    var factory     = sp.GetRequiredService<DataverseClientFactory>();
    var orgService  = factory.CreateOrganizationService();
    var appSettings = sp.GetRequiredService<AppSettings>();
    var logger      = sp.GetRequiredService<ILogger<ProductGroupSyncService>>();
    return new ProductGroupSyncService(orgService, appSettings, logger);
});

builder.Build().Run();
