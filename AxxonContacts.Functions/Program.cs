using Azure.Identity;
using Azure.Messaging.ServiceBus;
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
            DataverseUrl          = context.Configuration["DataverseUrl"] ?? string.Empty,
            ServiceBusQueueName   = context.Configuration["ServiceBusQueueName"] ?? string.Empty,
            DataverseClientId     = context.Configuration["DataverseClientId"],
            DataverseClientSecret = context.Configuration["DataverseClientSecret"],
            ServiceBusConnection  = context.Configuration["ServiceBusConnection"],
            ServiceBusNamespace   = context.Configuration["ServiceBusConnection__fullyQualifiedNamespace"]
        };

        if (string.IsNullOrWhiteSpace(settings.DataverseUrl))
            throw new InvalidOperationException(
                "La variable de entorno 'DataverseUrl' no esta configurada.");

        services.AddSingleton(settings);

        // ServiceBusClient singleton para renovacion directa de locks (evita el gRPC del host).
        // Managed Identity (produccion): ServiceBusConnection__fullyQualifiedNamespace
        // Connection string (local/SAS):  ServiceBusConnection
        ServiceBusClient sbClient = !string.IsNullOrEmpty(settings.ServiceBusNamespace)
            ? new ServiceBusClient(settings.ServiceBusNamespace, new DefaultAzureCredential())
            : new ServiceBusClient(settings.ServiceBusConnection
                ?? throw new InvalidOperationException(
                    "Configurar 'ServiceBusConnection' o 'ServiceBusConnection__fullyQualifiedNamespace'."));
        services.AddSingleton(sbClient);

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

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

await host.RunAsync();
