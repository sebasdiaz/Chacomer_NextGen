using System.Text.Json.Serialization;

namespace AxxonThinkchat.Functions.Models
{
    /// <summary>
    /// Pedido de envio de un mensaje de texto libre en sesion (accion send_text_msg),
    /// tal como lo recibe la Function por HTTP.
    ///
    /// No incluye "from": la linea emisora es dato de ambiente (App Setting
    /// ThinkchatFrom), no del caller. Tampoco incluye "action": la arma el servicio.
    ///
    /// A diferencia de las plantillas, este mensaje solo llega si la conversacion tiene
    /// la ventana de 24 horas abierta — y esa ventana la abre unicamente un mensaje
    /// ENTRANTE del cliente (regla de la plataforma de WhatsApp, no de Thinkchat).
    /// No existe un action para abrirla desde este lado: el camino para iniciar una
    /// conversacion es send_template y que el cliente responda.
    /// </summary>
    public class SendTextMessageRequest
    {
        /// <summary>Numero destino en formato internacional sin "+". Ej: 595981000000.</summary>
        [JsonPropertyName("to")]
        public string? To { get; set; }

        /// <summary>Texto libre del mensaje.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
