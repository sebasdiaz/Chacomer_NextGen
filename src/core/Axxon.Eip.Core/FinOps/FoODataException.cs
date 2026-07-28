using System.Net;
using System.Text.Json;

namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Error devuelto por la OData API de F&amp;O, con el status y el mensaje de negocio
    /// ya extraidos.
    ///
    /// Hereda de <see cref="HttpRequestException"/> a proposito: el codigo que ya
    /// capturaba esa excepcion sigue funcionando igual, y el que necesita distinguir
    /// entre "reintentar sirve" y "reintentar no sirve" usa <see cref="IsPermanent"/>.
    ///
    /// El motivo: F&amp;O responde 400 ante violaciones de reglas de negocio (un customer
    /// group inexistente en la compania, un party que ya existe como prospect). Tratar
    /// eso como error transitorio hace que Service Bus lo reintente hasta agotar el
    /// delivery count, martillando F&amp;O sin ninguna chance de exito.
    /// </summary>
    public sealed class FoODataException : HttpRequestException
    {
        private const int MaxDetailLength = 4000;

        private FoODataException(string message, HttpStatusCode status, string entitySet, string body, string? foMessage)
            : base(message)
        {
            Status       = status;
            EntitySet    = entitySet;
            ResponseBody = body;
            FoMessage    = foMessage;
        }

        public HttpStatusCode Status { get; }

        public string EntitySet { get; }

        /// <summary>Cuerpo crudo de la respuesta, para el log.</summary>
        public string ResponseBody { get; }

        /// <summary>
        /// Mensaje de negocio de F&amp;O (<c>error.innererror.message</c>, que trae el
        /// Infolog), o null si la respuesta no tenia ese formato.
        /// </summary>
        public string? FoMessage { get; }

        /// <summary>
        /// true cuando reintentar no puede cambiar el resultado: el mensaje tiene que ir
        /// al dead-letter en vez de consumir reintentos.
        ///
        /// Se consideran transitorios dentro del rango 4xx:
        ///   - 429 (throttling): ya lo reintenta el resilience handler del HttpClient.
        ///   - 408 (timeout): el request no llego a procesarse.
        ///   - 401/403: normalmente es un token vencido o una asignacion de permisos que
        ///     todavia no propago; un reintento puede resolverlo.
        /// Todo el resto de 4xx es permanente. Los 5xx quedan como transitorios.
        /// </summary>
        public bool IsPermanent =>
            (int)Status is >= 400 and < 500
            && Status is not (HttpStatusCode.TooManyRequests
                              or HttpStatusCode.RequestTimeout
                              or HttpStatusCode.Unauthorized
                              or HttpStatusCode.Forbidden);

        /// <summary>Texto para el dead-letter: el mensaje de F&amp;O si se pudo extraer, o el cuerpo.</summary>
        public string Detail
        {
            get
            {
                var detail = string.IsNullOrWhiteSpace(FoMessage) ? ResponseBody : FoMessage;
                return detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail;
            }
        }

        public static FoODataException FromResponse(HttpStatusCode status, string entitySet, string body)
        {
            var foMessage = ExtractFoMessage(body);

            var message = $"F&O OData respondio con HTTP {(int)status} en {entitySet}: " +
                          (foMessage ?? body);

            return new FoODataException(message, status, entitySet, body, foMessage);
        }

        /// <summary>
        /// Saca el mensaje util de la respuesta de error de F&amp;O. El formato es:
        /// <code>
        /// { "error": { "message": "An error has occurred.",
        ///              "innererror": { "message": "... Infolog: ...", "stacktrace": "..." } } }
        /// </code>
        /// El de afuera siempre dice "An error has occurred", asi que el que sirve es el
        /// de <c>innererror</c>. El stacktrace se descarta.
        /// </summary>
        private static string? ExtractFoMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty("error", out var error))
                    return null;

                if (error.TryGetProperty("innererror", out var inner)
                    && inner.TryGetProperty("message", out var innerMessage)
                    && innerMessage.ValueKind == JsonValueKind.String)
                {
                    var text = innerMessage.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }

                if (error.TryGetProperty("message", out var outerMessage)
                    && outerMessage.ValueKind == JsonValueKind.String)
                    return outerMessage.GetString()?.Trim();

                return null;
            }
            catch (JsonException)
            {
                // F&O no siempre devuelve JSON (ej: una pagina de error del gateway).
                return null;
            }
        }
    }
}
