namespace AxxonThinkchat.Functions.Configuration
{
    /// <summary>
    /// Settings de la API de Thinkchat. La conexion a Dataverse se configura via
    /// Axxon.Eip.Core (DataverseUrl, DataverseClientId/Secret o Managed Identity).
    ///
    /// PENDIENTE DE CONFIRMAR contra la collection de Postman: la URL base y el
    /// esquema de auth estan puestos como valores por defecto razonables, no
    /// verificados contra el servicio real.
    /// </summary>
    public class ThinkchatOptions
    {
        /// <summary>URL base de la API, con barra final. App Setting "ThinkchatBaseUrl".</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Path relativo del endpoint que devuelve los templates (get_template).
        /// App Setting "ThinkchatTemplatesPath".
        /// </summary>
        public string TemplatesPath { get; set; } = "get_template";

        /// <summary>
        /// Numero emisor que va en el body del request ("from"). App Setting
        /// "ThinkchatFrom" — en INTE: 595215180000. No se hardcodea un default:
        /// es dato de ambiente, no de codigo.
        /// </summary>
        public string From { get; set; } = string.Empty;

        /// <summary>
        /// Token/API key. Se resuelve con EipSecretResolver desde Key Vault: el secret
        /// se llama "secretThinkChat" (sobrescribible con el app setting
        /// "ThinkchatApiKeyName" si en otro ambiente cambia).
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Header en el que viaja la credencial. Default "Authorization" con esquema Bearer.
        /// Si Thinkchat usa un header propio (ej. "x-api-key"), setear
        /// "ThinkchatAuthHeader" y dejar "ThinkchatAuthScheme" vacio.
        /// </summary>
        public string AuthHeader { get; set; } = "Authorization";

        /// <summary>Esquema del header de auth. Vacio = el valor va crudo, sin prefijo.</summary>
        public string AuthScheme { get; set; } = "Bearer";

        /// <summary>Timeout de la llamada HTTP. Default 60s.</summary>
        public int TimeoutSeconds { get; set; } = 60;
    }
}
