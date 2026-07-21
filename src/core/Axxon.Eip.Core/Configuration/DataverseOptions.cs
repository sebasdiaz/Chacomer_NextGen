namespace Axxon.Eip.Core.Configuration
{
    /// <summary>
    /// Conexion a Dataverse. Se bindea desde claves planas de configuracion
    /// (Application Settings / local.settings.json / Key Vault):
    ///   DataverseUrl, DataverseClientId, DataverseClientSecret.
    /// </summary>
    public sealed class DataverseOptions
    {
        /// <summary>URL del environment. Ejemplo: https://tuorg.crm.dynamics.com</summary>
        public string Url { get; init; } = string.Empty;

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
