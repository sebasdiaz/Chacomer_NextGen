using Axxon.Eip.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Fiscal
{
    /// <summary>
    /// Registra los clientes HTTP de consultas fiscales del Paraguay en el contenedor
    /// de DI, siguiendo el mismo patron que <c>AddEipFoOData</c>:
    ///   - <see cref="AddEipSetApi"/>: API oficial de la SET (DNIT). Clave "SetApiKey".
    ///   - <see cref="AddEipTuruc"/>:  API publica de TURUC (sin credenciales).
    /// </summary>
    public static class EipFiscalExtensions
    {
        /// <summary>
        /// Registra <see cref="SetApiService"/> con el HttpClient nombrado "SetApi"
        /// apuntando a la API oficial de la SET Paraguay. El API Key se lee de la
        /// clave de configuracion "SetApiKey" (en Azure: secret de Key Vault). Si el
        /// secret del vault se llama distinto, indicarlo en el app setting
        /// "SetApiKeyName" (ver <see cref="EipSecretResolver"/>).
        /// </summary>
        public static IServiceCollection AddEipSetApi(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = new SetApiOptions
            {
                ApiKey = configuration.ResolveSecret("SetApiKey")
            };

            services.AddSingleton(options);

            services.AddHttpClient(SetApiService.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://servicios.set.gov.py/EsetApiWS/ApiWS/");
                client.Timeout     = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            services.AddTransient<SetApiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(SetApiService.HttpClientName);
                return new SetApiService(
                    httpClient,
                    sp.GetRequiredService<SetApiOptions>(),
                    sp.GetRequiredService<ILogger<SetApiService>>());
            });

            return services;
        }

        /// <summary>
        /// Registra <see cref="TurucApiService"/> con el HttpClient nombrado "TurucApi"
        /// apuntando a la API publica de TURUC (https://turuc.com.py). No requiere credenciales.
        /// </summary>
        public static IServiceCollection AddEipTuruc(this IServiceCollection services)
        {
            services.AddHttpClient(TurucApiService.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://turuc.com.py/api/contribuyente/");
                client.Timeout     = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            services.AddTransient<TurucApiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(TurucApiService.HttpClientName);
                return new TurucApiService(
                    httpClient,
                    sp.GetRequiredService<ILogger<TurucApiService>>());
            });

            return services;
        }
    }
}
