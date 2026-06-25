using Azure.Identity;
using AxxonProductGroups.Functions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace AxxonProductGroups.Functions.Services
{
    public class DataverseClientFactory
    {
        private readonly AppSettings _settings;
        private readonly ILogger<DataverseClientFactory> _logger;

        public DataverseClientFactory(AppSettings settings, ILogger<DataverseClientFactory> logger)
        {
            _settings = settings;
            _logger   = logger;
        }

        public IOrganizationService CreateOrganizationService()
        {
            if (string.IsNullOrWhiteSpace(_settings.DataverseUrl))
                throw new InvalidOperationException(
                    "DataverseUrl no esta configurado. Verificar Application Settings de la Function App.");

            if (_settings.UseClientSecretAuth)
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

            var client = new ServiceClient(
                new Uri(_settings.DataverseUrl),
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
            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={_settings.DataverseUrl};" +
                $"ClientId={_settings.DataverseClientId};" +
                $"ClientSecret={_settings.DataverseClientSecret};";

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
