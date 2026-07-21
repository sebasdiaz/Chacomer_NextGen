namespace Axxon.Eip.Core.Configuration
{
    /// <summary>
    /// Conexion a la OData API de Finance &amp; Operations. Se bindea desde claves
    /// planas de configuracion (Application Settings / local.settings.json / Key Vault):
    ///   FoBaseUrl, FoTenantId, FoClientId, FoClientSecret.
    /// </summary>
    public sealed class FoODataOptions
    {
        /// <summary>URL base del environment de F&amp;O. Ejemplo: https://tuorg.operations.dynamics.com</summary>
        public string BaseUrl { get; init; } = string.Empty;

        /// <summary>Tenant ID de Entra ID. Solo para desarrollo local / DESA.</summary>
        public string? TenantId { get; init; }

        /// <summary>Client ID del App Registration. Solo para desarrollo local / DESA.</summary>
        public string? ClientId { get; init; }

        /// <summary>Client Secret del App Registration. Solo para desarrollo local / DESA.</summary>
        public string? ClientSecret { get; init; }

        /// <summary>
        /// True si se deben usar credenciales de Service Principal en lugar de Managed Identity.
        /// Se activa automaticamente si ClientId y ClientSecret estan presentes.
        /// </summary>
        public bool UseClientSecretAuth =>
            !string.IsNullOrEmpty(ClientId) &&
            !string.IsNullOrEmpty(ClientSecret);
    }
}
