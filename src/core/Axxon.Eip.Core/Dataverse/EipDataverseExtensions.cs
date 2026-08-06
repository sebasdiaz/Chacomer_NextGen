using Axxon.Eip.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>
    /// Registra la conexion a Dataverse en el contenedor de DI.
    /// Claves de configuracion: DataverseUrl (obligatoria), DataverseClientId,
    /// DataverseClientSecret (solo DESA/INTE — en Key Vault). Si el secret del vault
    /// se llama distinto, indicarlo en el app setting "DataverseClientSecretName"
    /// (ver <see cref="EipSecretResolver"/>).
    /// </summary>
    public static class EipDataverseExtensions
    {
        public static IServiceCollection AddEipDataverse(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = new DataverseOptions
            {
                Url          = configuration["DataverseUrl"] ?? string.Empty,
                ClientId     = configuration["DataverseClientId"],
                ClientSecret = configuration.ResolveSecret("DataverseClientSecret")
            };

            if (string.IsNullOrWhiteSpace(options.Url))
                throw new InvalidOperationException(
                    "La variable de entorno 'DataverseUrl' no esta configurada.");

            services.AddSingleton(options);

            // Transient: cada invocacion obtiene su propio ServiceClient.
            services.AddTransient<DataverseClientFactory>();

            return services;
        }
    }
}
