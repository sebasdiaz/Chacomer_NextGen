using AxxonContacts.Functions.Configuration;
using AxxonContacts.Functions.Services;
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
            DataverseUrl               = context.Configuration["DataverseUrl"] ?? string.Empty,
            ServiceBusQueueName        = context.Configuration["ServiceBusQueueName"] ?? string.Empty,
            AccountServiceBusQueueName = context.Configuration["AccountServiceBusQueueName"] ?? string.Empty,
            DataverseClientId     = context.Configuration["DataverseClientId"],
            DataverseClientSecret = context.Configuration["DataverseClientSecret"],
            SetApiKey             = context.Configuration["SetApiKey"],
            FoBaseUrl             = context.Configuration["FoBaseUrl"] ?? string.Empty,
            FoTenantId            = context.Configuration["FoTenantId"],
            FoClientId            = context.Configuration["FoClientId"],
            FoClientSecret        = context.Configuration["FoClientSecret"]
        };

        if (string.IsNullOrWhiteSpace(settings.DataverseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'DataverseUrl' no esta configurada.");

        services.AddSingleton(settings);

        // DataverseClientFactory Transient: cada invocacion obtiene su propio ServiceClient.
        // Sessions de Service Bus garantizan maxConcurrentCallsPerSession=1 por cliente,
        // por lo que un ServiceClient por invocacion es seguro sin estado compartido.
        services.AddTransient<DataverseClientFactory>();
        services.AddTransient<MasterMatchingService>(sp =>
        {
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<MasterMatchingService>>();
            return new MasterMatchingService(orgService, logger);
        });

        services.AddTransient<AccountMasterMatchingService>(sp =>
        {
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<AccountMasterMatchingService>>();
            return new AccountMasterMatchingService(orgService, logger);
        });

        // HttpClient para la API de TURUC (RUC validation)
        services.AddHttpClient("RucApi", client =>
        {
            client.BaseAddress = new Uri("https://turuc.com.py/api/contribuyente/");
            client.Timeout     = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddTransient<RucValidationService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("RucApi");
            var factory           = sp.GetRequiredService<DataverseClientFactory>();
            var orgService        = factory.CreateOrganizationService();
            var logger            = sp.GetRequiredService<ILogger<RucValidationService>>();
            return new RucValidationService(httpClient, orgService, logger);
        });

        services.AddTransient<SetRucValidationService>(sp =>
        {
            var setApi     = sp.GetRequiredService<SetApiService>();
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<SetRucValidationService>>();
            return new SetRucValidationService(setApi, orgService, logger);
        });

        services.AddTransient<ContactProcessingService>(sp =>
        {
            var masterMatchingService   = sp.GetRequiredService<MasterMatchingService>();
            var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
            var logger                  = sp.GetRequiredService<ILogger<ContactProcessingService>>();
            return new ContactProcessingService(masterMatchingService, setRucValidationService, logger);
        });

        services.AddTransient<AccountProcessingService>(sp =>
        {
            var matchingService        = sp.GetRequiredService<AccountMasterMatchingService>();
            var setRucValidationService = sp.GetRequiredService<SetRucValidationService>();
            var logger                  = sp.GetRequiredService<ILogger<AccountProcessingService>>();
            return new AccountProcessingService(matchingService, setRucValidationService, logger);
        });

        // TurucApiService: proxy HTTP a la API publica de TURUC.
        // Reutiliza el mismo HttpClient "RucApi" (mismo base address).
        services.AddTransient<TurucApiService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("RucApi");
            var logger            = sp.GetRequiredService<ILogger<TurucApiService>>();
            return new TurucApiService(httpClient, logger);
        });

        // HttpClient para la API oficial de la SET (Subsecretaria de Estado de Tributacion).
        services.AddHttpClient("SetApi", client =>
        {
            client.BaseAddress = new Uri("https://servicios.set.gov.py/EsetApiWS/ApiWS/");
            client.Timeout     = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // SetApiService: proxy HTTP a la API oficial de la SET Paraguay.
        services.AddTransient<SetApiService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("SetApi");
            var appSettings       = sp.GetRequiredService<AppSettings>();
            var logger            = sp.GetRequiredService<ILogger<SetApiService>>();
            return new SetApiService(httpClient, appSettings, logger);
        });

        // HttpClient para F&O OData (ReleasedProductsV2)
        services.AddHttpClient("FoOData", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        });

        services.AddTransient<IFoDataService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient        = httpClientFactory.CreateClient("FoOData");
            var appSettings       = sp.GetRequiredService<AppSettings>();
            var logger            = sp.GetRequiredService<ILogger<FoDataService>>();
            return new FoDataService(httpClient, appSettings, logger);
        });

        services.AddTransient<ISharedProductSyncService>(sp =>
        {
            var factory    = sp.GetRequiredService<DataverseClientFactory>();
            var orgService = factory.CreateOrganizationService();
            var logger     = sp.GetRequiredService<ILogger<SharedProductSyncService>>();
            return new SharedProductSyncService(orgService, logger);
        });

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

await host.RunAsync();
