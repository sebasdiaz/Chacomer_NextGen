using AxxonProductGroups.Functions.Configuration;
using AxxonProductGroups.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var settings = new AppSettings
        {
            DataverseUrl          = context.Configuration["DataverseUrl"] ?? string.Empty,
            DataverseClientId     = context.Configuration["DataverseClientId"],
            DataverseClientSecret = context.Configuration["DataverseClientSecret"],
            FoBaseUrl             = context.Configuration["FoBaseUrl"] ?? string.Empty,
            FoTenantId            = context.Configuration["FoTenantId"],
            FoClientId            = context.Configuration["FoClientId"],
            FoClientSecret        = context.Configuration["FoClientSecret"]
        };

        if (string.IsNullOrWhiteSpace(settings.DataverseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'DataverseUrl' no esta configurada.");

        if (string.IsNullOrWhiteSpace(settings.FoBaseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'FoBaseUrl' no esta configurada.");

        services.AddSingleton(settings);

        // DataverseClientFactory Transient: cada invocacion obtiene su propio ServiceClient.
        services.AddTransient<DataverseClientFactory>();

        services.AddHttpClient("FoOData", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        });

        services.AddTransient<IFoProductGroupService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("FoOData");
            var appSettings       = sp.GetRequiredService<AppSettings>();
            var logger            = sp.GetRequiredService<ILogger<FoProductGroupService>>();
            return new FoProductGroupService(httpClient, appSettings, logger);
        });

        services.AddTransient<IProductGroupSyncService>(sp =>
        {
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<ProductGroupSyncService>>();
            return new ProductGroupSyncService(orgService, logger);
        });

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

await host.RunAsync();
