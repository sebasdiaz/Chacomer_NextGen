using System.Text.Json.Serialization;

namespace AxxonTicketAtencion.Functions.Models
{
    /// <summary>Body del POST que manda el web resource del formulario de Cita de Servicio.</summary>
    public sealed class GenerarTicketAtencionRequest
    {
        [JsonPropertyName("serviceAppointmentId")]
        public string? ServiceAppointmentId { get; set; }
    }

    /// <summary>Estados posibles de la respuesta.</summary>
    public static class TicketStatus
    {
        /// <summary>Word generado y PDF adjuntado en SharePoint.</summary>
        public const string Ok = "OK";

        /// <summary>
        /// Word generado, pero fallo la conversion a PDF o el adjunto en SharePoint.
        /// <c>url</c> viene vacia: el cliente DEBE caer a <c>wordBase64</c>.
        /// </summary>
        public const string OkSinPdf = "OK_SIN_PDF";

        /// <summary>No se pudo generar el documento.</summary>
        public const string Error = "ERROR";
    }

    /// <summary>Respuesta exitosa (200), con o sin PDF.</summary>
    public sealed class GenerarTicketAtencionResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = TicketStatus.Ok;

        [JsonPropertyName("wordBase64")]
        public string WordBase64 { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>URL del PDF en SharePoint. Vacia cuando el status es OK_SIN_PDF.</summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("wordBytes")]
        public int WordBytes { get; set; }
    }

    /// <summary>
    /// Respuesta de error (4xx/5xx). Solo lleva un mensaje apto para mostrarle al usuario
    /// final: el stack trace va a Application Insights y nunca al cliente.
    /// </summary>
    public sealed class TicketErrorResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = TicketStatus.Error;

        [JsonPropertyName("mensaje")]
        public string Mensaje { get; set; } = string.Empty;
    }
}
