using Axxon.Eip.Core.Configuration;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>
    /// Fabrica de IOrganizationService para las Azure Functions de la EiP.
    /// Produce un ServiceClient conectado a Dataverse via:
    ///   - Managed Identity (produccion): sin secrets, rotacion automatica.
    ///   - Client Secret (desa/fallback): cuando DataverseClientId y DataverseClientSecret estan seteados.
    /// </summary>
    public class DataverseClientFactory
    {
        private readonly DataverseOptions _options;
        private readonly ILogger<DataverseClientFactory> _logger;

        public DataverseClientFactory(DataverseOptions options, ILogger<DataverseClientFactory> logger)
        {
            _options = options;
            _logger  = logger;
        }

        /// <summary>
        /// Crea un IOrganizationService listo para usar.
        /// Cada llamada crea una nueva instancia (useUniqueInstance = true).
        ///
        /// PREREQUISITO para Managed Identity en produccion:
        ///   1. Habilitar System Assigned Managed Identity en la Function App.
        ///   2. Agregar la MI como Application User en Power Platform Admin Center:
        ///      Admin Center → Environments → Tu Env → Settings → Users → App Users → New App User
        ///      → Seleccionar la MI → Asignar rol de seguridad (ej: System Administrator o rol custom)
        /// </summary>
        public IOrganizationService CreateOrganizationService()
        {
            if (string.IsNullOrWhiteSpace(_options.Url))
                throw new InvalidOperationException(
                    "DataverseUrl no esta configurado. Verificar Application Settings de la Function App.");

            if (_options.UseClientSecretAuth)
            {
                _logger.LogInformation(
                    "[DataverseClientFactory] Usando Client Secret (modo DESA). " +
                    "Configurar Managed Identity para produccion.");
                return CreateWithClientSecret();
            }

            _logger.LogInformation("[DataverseClientFactory] Usando Managed Identity.");
            return CreateWithManagedIdentity();
        }

        private IOrganizationService CreateWithManagedIdentity()
        {
            var credential = new DefaultAzureCredential();

            // ServiceClient no acepta TokenCredential directamente —
            // usa un callback async que recibe el resource URI y devuelve el token.
            var client = new ServiceClient(
                new Uri(_options.Url),
                async (string resource) =>
                {
                    var token = await credential.GetTokenAsync(
                        new Azure.Core.TokenRequestContext(new[] { resource + "/.default" }));
                    return token.Token;
                },
                useUniqueInstance: true);

            ValidateConnection(client);
            return client;
        }

        private IOrganizationService CreateWithClientSecret()
        {
            // Connection string con Client ID/Secret para desarrollo local
            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={_options.Url};" +
                $"ClientId={_options.ClientId};" +
                $"ClientSecret={_options.ClientSecret};";

            var client = new ServiceClient(connectionString);
            ValidateConnection(client);
            return client;
        }

        private void ValidateConnection(ServiceClient client)
        {
            if (!client.IsReady)
                throw new InvalidOperationException(
                    $"ServiceClient no pudo conectar a Dataverse: {client.LastError}");

            _logger.LogDebug(
                "[DataverseClientFactory] Conectado a Dataverse. OrganizationId={OrganizationId}",
                client.ConnectedOrgId);
        }
    }
}
