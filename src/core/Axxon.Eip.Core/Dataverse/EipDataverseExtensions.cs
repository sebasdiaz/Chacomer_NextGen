using Axxon.Eip.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>
    /// Registra la conexion a Dataverse en el contenedor de DI.
    /// Claves de configuracion: DataverseUrl (obligatoria), DataverseClientId,
    /// DataverseClientSecret (solo DESA — en Key Vault como secret "DataverseClientSecret").
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
                ClientSecret = configuration["DataverseClientSecret"]
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
