using Axxon.Eip.Core.Configuration;
using Axxon.Eip.Core.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Graph
{
    /// <summary>
    /// Registra el cliente de Microsoft Graph / SharePoint, siguiendo el mismo patron que
    /// <c>AddEipDataverseWebApi</c>: HttpClient nombrado via IHttpClientFactory y
    /// TokenCredential singleton (Azure.Identity cachea los tokens por instancia).
    ///
    /// Claves de configuracion:
    ///   SharePointSiteUrl   (obligatoria) — sitio sobre el que opera la integracion.
    ///   GraphClientId       — App Registration con los app roles de Graph. Vacio = Managed Identity.
    ///   GraphClientSecret   — secret del registration. En Azure sale de Key Vault; si el
    ///                         secret se llama distinto, usar "GraphClientSecretName".
    ///   GraphTenantId       — tenant de Entra. Solo hace falta con ClientId + ClientSecret.
    ///
    /// PERMISOS: la conversion a PDF y el upload usan permisos de APLICACION
    /// (Sites.ReadWrite.All). Pedirlos en el App Registration no alcanza: necesitan
    /// consentimiento de administrador. Sin el consentimiento, Graph responde 403.
    /// </summary>
    public static class EipGraphExtensions
    {
        public static IServiceCollection AddEipGraph(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = new GraphOptions
            {
                SharePointSiteUrl = configuration["SharePointSiteUrl"] ?? string.Empty,
                ClientId          = configuration["GraphClientId"],
                ClientSecret      = configuration.ResolveSecret("GraphClientSecret"),
                TenantId          = configuration["GraphTenantId"]
            };

            services.AddSingleton(options);

            services.AddHttpClient(GraphSharePointService.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(GraphSharePointService.BaseAddress);
                // La conversion a PDF de un documento grande no es instantanea.
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            services.AddSingleton<IGraphSharePointService>(sp =>
            {
                var opts = sp.GetRequiredService<GraphOptions>();

                return new GraphSharePointService(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    EipCredentialFactory.Create(opts.TenantId, opts.ClientId, opts.ClientSecret),
                    opts,
                    sp.GetRequiredService<ILogger<GraphSharePointService>>());
            });

            return services;
        }
    }
}
