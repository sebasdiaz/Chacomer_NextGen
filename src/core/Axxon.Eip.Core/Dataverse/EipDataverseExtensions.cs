using Axxon.Eip.Core.Configuration;
using Axxon.Eip.Core.Identity;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            services.AddEipDataverseOptions(configuration);

            // Transient: cada invocacion obtiene su propio ServiceClient.
            services.AddTransient<DataverseClientFactory>();

            return services;
        }

        /// <summary>
        /// Registra <see cref="IDataverseWebApiClient"/>: acceso OData a Dataverse para las
        /// consultas que no se expresan bien en FetchXML ($expand anidados, $orderby+$top).
        ///
        /// Convive con <see cref="AddEipDataverse"/> —ambos comparten
        /// <see cref="DataverseOptions"/>— y puede registrarse solo, sin el SDK.
        ///
        /// El <see cref="TokenCredential"/> va como singleton a proposito: Azure.Identity
        /// cachea los tokens por instancia. El HttpClient viene de IHttpClientFactory y el
        /// token se agrega por request, nunca en DefaultRequestHeaders.
        /// </summary>
        public static IServiceCollection AddEipDataverseWebApi(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = services.AddEipDataverseOptions(configuration);

            services.AddHttpClient(DataverseWebApiClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(options.Url.TrimEnd('/') + DataverseWebApiClient.ApiPath);
                client.Timeout     = TimeSpan.FromSeconds(60);
            });

            services.AddSingleton<IDataverseWebApiClient>(sp =>
            {
                var opts = sp.GetRequiredService<DataverseOptions>();

                return new DataverseWebApiClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    EipCredentialFactory.Create(opts.TenantId, opts.ClientId, opts.ClientSecret),
                    opts,
                    sp.GetRequiredService<ILogger<DataverseWebApiClient>>());
            });

            return services;
        }

        /// <summary>
        /// Bindea y registra <see cref="DataverseOptions"/> una sola vez, para que una app
        /// pueda pedir el SDK y la Web API sin duplicar el registro.
        /// </summary>
        private static DataverseOptions AddEipDataverseOptions(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var existing = services
                .FirstOrDefault(d => d.ServiceType == typeof(DataverseOptions))
                ?.ImplementationInstance as DataverseOptions;

            if (existing is not null)
                return existing;

            var options = new DataverseOptions
            {
                Url          = configuration["DataverseUrl"] ?? string.Empty,
                ClientId     = configuration["DataverseClientId"],
                ClientSecret = configuration.ResolveSecret("DataverseClientSecret"),
                TenantId     = configuration["DataverseTenantId"]
            };

            if (string.IsNullOrWhiteSpace(options.Url))
                throw new InvalidOperationException(
                    "La variable de entorno 'DataverseUrl' no esta configurada.");

            services.AddSingleton(options);
            return options;
        }
    }
}
