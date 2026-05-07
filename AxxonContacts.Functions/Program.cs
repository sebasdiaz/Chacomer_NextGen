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
            DataverseClientSecret = context.Configuration["DataverseClientSecret"]
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

        services.AddLogging(b => b.AddConsole());
    })
    .Build();

await host.RunAsync();
