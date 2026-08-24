namespace Axxon.Eip.Core.Graph
{
    /// <summary>
    /// Conexion a Microsoft Graph. Se bindea desde claves planas de configuracion:
    ///   GraphTenantId, GraphClientId, GraphClientSecret, SharePointSiteUrl.
    ///
    /// Sin ClientId/ClientSecret la app autentica con su Managed Identity — que es el
    /// estado deseado, pero exige que la MI tenga asignados los app roles de Graph
    /// (Sites.ReadWrite.All) igual que los tendria un App Registration.
    /// </summary>
    public sealed class GraphOptions
    {
        /// <summary>Tenant de Entra. Obligatorio solo si se autentica por Service Principal.</summary>
        public string? TenantId { get; init; }

        /// <summary>Client ID del App Registration con los permisos de aplicacion de Graph.</summary>
        public string? ClientId { get; init; }

        /// <summary>Client Secret del App Registration. En Azure sale de Key Vault.</summary>
        public string? ClientSecret { get; init; }

        /// <summary>
        /// URL del sitio de SharePoint sobre el que opera la integracion.
        /// Ej: https://contoso.sharepoint.com/sites/B1-Chacomer-INTE
        /// </summary>
        public string SharePointSiteUrl { get; init; } = string.Empty;

        public bool UseClientSecretAuth =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret);
    }
}
