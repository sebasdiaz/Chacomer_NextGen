namespace Axxon.Eip.Core.Fiscal
{
    /// <summary>
    /// Configuracion del cliente HTTP hacia la API oficial de la SET Paraguay (DNIT).
    /// Se bindea desde la clave plana de configuracion "SetApiKey"
    /// (Application Settings / local.settings.json / Key Vault secret "SetApiKey").
    /// </summary>
    public sealed class SetApiOptions
    {
        /// <summary>
        /// API Key para el servicio oficial de la SET Paraguay.
        /// Endpoint: https://servicios.set.gov.py/EsetApiWS/ApiWS/
        /// En Azure se resuelve desde Key Vault (secret "SetApiKey").
        /// </summary>
        public string? ApiKey { get; init; }
    }
}
