using AxxonThinkchat.Functions.Models;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>Envio de mensajes salientes por la API de Thinkchat.</summary>
    public interface IThinkchatMessageService
    {
        /// <summary>
        /// Envia una plantilla HSM de WhatsApp (accion send_template).
        /// </summary>
        Task<SendTemplateResult> SendTemplateAsync(
            SendTemplateRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Envia un mensaje de texto libre en sesion (accion send_text_msg). Requiere
        /// la ventana de 24 horas abierta; si esta cerrada el proveedor rechaza y el
        /// resultado trae su body crudo.
        /// </summary>
        Task<SendTemplateResult> SendTextMessageAsync(
            SendTextMessageRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Resultado del envio. Se devuelve el body crudo del proveedor sin
    /// reinterpretarlo: la doc no publica contrato de respuesta, asi que el caller
    /// necesita verlo tal cual para relevarlo.
    /// </summary>
    /// <param name="Accepted">true si la API acepto el envio.</param>
    /// <param name="StatusCode">HTTP que devolvio Thinkchat.</param>
    /// <param name="RawBody">Body crudo de la respuesta.</param>
    /// <param name="Message">Campo "msg" del proveedor, si vino.</param>
    public record SendTemplateResult(
        bool Accepted,
        int StatusCode,
        string RawBody,
        string? Message);
}
