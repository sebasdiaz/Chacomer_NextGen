namespace AxxonThinkchat.Functions.Configuration
{
    /// <summary>
    /// Settings de la API de Thinkchat. La conexion a Dataverse se configura via
    /// Axxon.Eip.Core (DataverseUrl, DataverseClientId/Secret o Managed Identity).
    ///
    /// La API no es REST por recurso: es RPC sobre un endpoint unico. Todas las
    /// operaciones son POST contra la URL base y el verbo logico viaja en el campo
    /// "action" del body. Verificado contra la collection de Postman del proveedor.
    /// </summary>
    public class ThinkchatOptions
    {
        /// <summary>URL base de la API, con barra final. App Setting "ThinkchatBaseUrl".</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Path relativo del endpoint. Vacio a proposito: el endpoint ES la URL base
        /// (ver <see cref="Action"/>). Queda como App Setting "ThinkchatTemplatesPath"
        /// por si el proveedor algun dia separa las operaciones por ruta.
        /// </summary>
        public string TemplatesPath { get; set; } = string.Empty;

        /// <summary>
        /// Verbo logico que va en el campo "action" del body. App Setting
        /// "ThinkchatTemplatesAction". Es "get_templates", en plural.
        /// </summary>
        public string Action { get; set; } = "get_templates";

        /// <summary>
        /// Verbo del body para el envio de plantillas. App Setting
        /// "ThinkchatSendTemplateAction". Default "send_template".
        /// </summary>
        public string SendTemplateAction { get; set; } = "send_template";

        /// <summary>
        /// Verbo del body para el texto libre en sesion. App Setting
        /// "ThinkchatSendTextAction". Default "send_text_msg".
        /// </summary>
        public string SendTextAction { get; set; } = "send_text_msg";

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
