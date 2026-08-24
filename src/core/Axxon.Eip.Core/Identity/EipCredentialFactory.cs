using Azure.Core;
using Azure.Identity;

namespace Axxon.Eip.Core.Identity
{
    /// <summary>
    /// Construye el <see cref="TokenCredential"/> con el que una integracion autentica
    /// contra un recurso de Entra (Dataverse Web API, Microsoft Graph, F&amp;O).
    ///
    /// Criterio unico para toda la EiP:
    ///   - Con ClientId + ClientSecret configurados -> Service Principal.
    ///   - Sin ellos -> <see cref="DefaultAzureCredential"/>, que en Azure resuelve a la
    ///     Managed Identity de la Function App y en local a `az login` / Visual Studio.
    ///
    /// El credential DEBE registrarse como singleton: Azure.Identity cachea los tokens
    /// por instancia, asi que uno nuevo por request implica un round-trip a Entra por
    /// llamada (es exactamente el problema que tenia la implementacion original de
    /// TicketAtencion, que construia un ConfidentialClientApplication por invocacion).
    /// </summary>
    public static class EipCredentialFactory
    {
        public static TokenCredential Create(string? tenantId, string? clientId, string? clientSecret)
        {
            var hasClientSecret =
                !string.IsNullOrWhiteSpace(clientId) &&
                !string.IsNullOrWhiteSpace(clientSecret);

            if (!hasClientSecret)
                return new DefaultAzureCredential();

            if (string.IsNullOrWhiteSpace(tenantId))
                throw new InvalidOperationException(
                    "Se configuro autenticacion por Service Principal (ClientId + ClientSecret) " +
                    "pero falta el TenantId. Sin tenant no se puede resolver la authority de Entra.");

            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }
    }
}
