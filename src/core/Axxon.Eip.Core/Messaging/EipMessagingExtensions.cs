using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axxon.Eip.Core.Messaging
{
    /// <summary>
    /// Registra el publisher del backbone de Service Bus.
    ///
    /// Usa la misma configuracion que el trigger de Functions, para que produccion y
    /// consumo no puedan apuntar a namespaces distintos:
    ///   - <c>ServiceBusConnection__fullyQualifiedNamespace</c> (Managed Identity), o
    ///   - <c>ServiceBusConnection</c> (connection string, solo DESA/local).
    /// </summary>
    public static class EipMessagingExtensions
    {
        public const string ConnectionName = "ServiceBusConnection";

        public static IServiceCollection AddEipServiceBusPublisher(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // El binding de Functions escribe la clave con "__"; el provider de
            // configuracion la expone con ":".
            var fullyQualifiedNamespace =
                configuration[$"{ConnectionName}:fullyQualifiedNamespace"] ??
                configuration[$"{ConnectionName}__fullyQualifiedNamespace"];

            var connectionString = configuration[ConnectionName];

            services.AddSingleton(_ =>
            {
                if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
                    return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());

                if (!string.IsNullOrWhiteSpace(connectionString))
                    return new ServiceBusClient(connectionString);

                throw new InvalidOperationException(
                    $"No hay configuracion de Service Bus: falta '{ConnectionName}__fullyQualifiedNamespace' " +
                    $"(Managed Identity) o '{ConnectionName}' (connection string).");
            });

            services.AddSingleton<IEipMessagePublisher, EipServiceBusPublisher>();

            return services;
        }
    }
}
