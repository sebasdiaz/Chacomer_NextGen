using System.Text.Json.Serialization;

namespace AxxonThinkchat.Functions.Models
{
    /// <summary>
    /// Pedido de envio de una plantilla HSM, tal como lo recibe la Function por HTTP.
    ///
    /// No incluye "from": la linea emisora es dato de ambiente (App Setting
    /// ThinkchatFrom), no del caller. Tampoco incluye "action": la arma el servicio.
    /// </summary>
    public class SendTemplateRequest
    {
        /// <summary>Numero destino en formato internacional sin "+". Ej: 595981000000.</summary>
        [JsonPropertyName("to")]
        public string? To { get; set; }

        /// <summary>Id de la plantilla, de los que sincroniza axx_metatemplates (axx_id).</summary>
        [JsonPropertyName("template_id")]
        public string? TemplateId { get; set; }

        /// <summary>
        /// Reemplazos posicionales de las variables de la plantilla. El orden y la
        /// cantidad deben coincidir con la plantilla aprobada por Meta — la API no
        /// valida esto del lado nuestro.
        /// </summary>
        [JsonPropertyName("template_params")]
        public List<string>? TemplateParams { get; set; }

        /// <summary>URL publica del adjunto. Solo si la plantilla tiene header de media.</summary>
        [JsonPropertyName("template_media")]
        public string? TemplateMedia { get; set; }

        /// <summary>Ruteo de la respuesta del cliente. Opcional.</summary>
        [JsonPropertyName("extras")]
        public SendTemplateExtras? Extras { get; set; }
    }

    /// <summary>
    /// Ruteo del inbound: a donde va la respuesta del cliente y por cuanto tiempo.
    ///
    /// El proveedor advierte que hay que usar **un solo tipo de destino a la vez**
    /// (bot / queue / agent). Su propio ejemplo los manda los cuatro juntos y
    /// contradice la nota, asi que la Function rechaza con 400 el pedido que trae mas
    /// de uno, en vez de dejar que la API elija en silencio.
    /// </summary>
    public class SendTemplateExtras
    {
        /// <summary>Ventana en MINUTOS durante la cual se rutea la respuesta.</summary>
        [JsonPropertyName("inbound_expiration")]
        public int? InboundExpiration { get; set; }

        [JsonPropertyName("inbound_bot")]
        public int? InboundBot { get; set; }

        [JsonPropertyName("inbound_queue")]
        public int? InboundQueue { get; set; }

        [JsonPropertyName("inbound_agent")]
        public int? InboundAgent { get; set; }

        /// <summary>Nombre de la interaccion. No es un destino: no cuenta para la regla.</summary>
        [JsonPropertyName("inbound_interaccion")]
        public string? InboundInteraccion { get; set; }

        /// <summary>Cuantos destinos de inbound vinieron seteados.</summary>
        [JsonIgnore]
        public int DestinationCount =>
            (InboundBot.HasValue ? 1 : 0) +
            (InboundQueue.HasValue ? 1 : 0) +
            (InboundAgent.HasValue ? 1 : 0);
    }
}
