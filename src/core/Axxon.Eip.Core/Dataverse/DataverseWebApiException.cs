using System.Net;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>
    /// Falla de una llamada a la Web API de Dataverse.
    ///
    /// Existe para que un error de una query NO pueda degradar el resultado en silencio.
    /// La implementacion original de TicketAtencion devolvia un array vacio ante cualquier
    /// error: si fallaba la query de trabajos, el ticket salia sin trabajos y nadie se
    /// enteraba. Con esta excepcion la falla sube y el caller decide.
    /// </summary>
    public sealed class DataverseWebApiException : Exception
    {
        public DataverseWebApiException(
            string label,
            HttpStatusCode statusCode,
            string requestUrl,
            string? responseBody)
            : base($"[{label}] Dataverse respondio {(int)statusCode} ({statusCode}). " +
                   $"URL: {requestUrl}. Detalle: {Truncate(responseBody)}")
        {
            Label       = label;
            StatusCode  = statusCode;
            RequestUrl  = requestUrl;
        }

        public string Label { get; }

        public HttpStatusCode StatusCode { get; }

        public string RequestUrl { get; }

        /// <summary>True si el recurso no existe (404): el caller suele querer un 404 propio.</summary>
        public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

        private static string Truncate(string? body) =>
            string.IsNullOrEmpty(body)
                ? "(sin body)"
                : body.Length > 600 ? body[..600] + "..." : body;
    }
}
