using System.Net;
using System.Text.Json;
using AxxonThinkchat.Functions.Models;
using AxxonThinkchat.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AxxonThinkchat.Functions.Functions
{
    /// <summary>
    /// Envia un mensaje de texto libre de WhatsApp en sesion (accion send_text_msg).
    ///
    /// Endpoint expuesto:
    ///   POST /api/thinkchat/send-text   (AuthorizationLevel.Function)
    ///
    /// Body esperado:
    /// <code>
    /// {
    ///   "to": "595981000000",
    ///   "text": "Hola, seguimos con tu consulta."
    /// }
    /// </code>
    ///
    /// El "from" NO se recibe: sale del App Setting ThinkchatFrom, igual que en
    /// send-template.
    ///
    /// **Este mensaje solo llega con la ventana de 24 horas abierta.** La ventana la
    /// abre unicamente un mensaje ENTRANTE del cliente (regla de la plataforma de
    /// WhatsApp, no de Thinkchat) y cada mensaje suyo la renueva. No existe un action
    /// para abrirla desde este lado: para iniciar una conversacion el camino es
    /// send-template (plantilla aprobada por Meta) y que el cliente responda. Por eso
    /// aca no se valida contra axx_metatemplates: no hay plantilla que validar.
    ///
    /// Respuestas:
    ///   200 — la API acepto el envio. Se devuelve su body crudo.
    ///   400 — el pedido no paso la validacion local (no se llamo a Thinkchat).
    ///   502 — Thinkchat rechazo o fallo; el caso esperable es la ventana cerrada.
    ///         Se devuelve su body crudo: el proveedor no documenta ese error y el
    ///         "msg" es lo unico que explica el motivo.
    /// </summary>
    public class ThinkchatSendTextFunction
    {
        /// <summary>Limite de WhatsApp para el body de un mensaje de texto.</summary>
        private const int MaxTextLength = 4096;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IThinkchatMessageService _messageService;
        private readonly ILogger<ThinkchatSendTextFunction> _logger;

        public ThinkchatSendTextFunction(
            IThinkchatMessageService messageService,
            ILogger<ThinkchatSendTextFunction> logger)
        {
            _messageService = messageService;
            _logger         = logger;
        }

        [Function("Thinkchat_SendText")]
        public async Task<HttpResponseData> SendText(
            [HttpTrigger(AuthorizationLevel.Function, "post",
                Route = "thinkchat/send-text")]
            HttpRequestData req,
            CancellationToken cancellationToken)
        {
            SendTextMessageRequest? request;

            try
            {
                request = await JsonSerializer.DeserializeAsync<SendTextMessageRequest>(
                    req.Body, JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[Thinkchat_SendText] Body invalido.");
                return await Error(req, HttpStatusCode.BadRequest, "El body no es JSON valido.");
            }

            if (request is null)
                return await Error(req, HttpStatusCode.BadRequest, "El body es requerido.");

            if (Validate(request) is { } motivo)
            {
                _logger.LogWarning(
                    "[Thinkchat_SendText] Pedido rechazado: {Motivo} To={To}",
                    motivo, request.To);

                return await Error(req, HttpStatusCode.BadRequest, motivo);
            }

            var result = await _messageService.SendTextMessageAsync(request, cancellationToken);

            // 502 y no el status del proveedor: para el caller esto es una falla del
            // upstream, y propagar un 401 de Thinkchat le haria pensar que su propia
            // llamada quedo sin autorizar.
            var status = result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadGateway;

            return await Passthrough(req, status, result.RawBody);
        }

        /// <summary>Validaciones locales. Devuelve el motivo, o null si el pedido esta bien.</summary>
        private static string? Validate(SendTextMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.To))
                return "El campo 'to' es requerido.";

            // Formato internacional sin "+", tal como lo pide la doc del proveedor.
            if (!request.To.All(char.IsDigit))
                return "El campo 'to' debe ser solo digitos, en formato internacional sin '+'.";

            if (string.IsNullOrWhiteSpace(request.Text))
                return "El campo 'text' es requerido.";

            if (request.Text.Length > MaxTextLength)
                return $"El campo 'text' supera el limite de {MaxTextLength} caracteres " +
                       $"de WhatsApp (llegaron {request.Text.Length}).";

            return null;
        }

        private static async Task<HttpResponseData> Passthrough(
            HttpRequestData req, HttpStatusCode status, string body)
        {
            var resp = req.CreateResponse(status);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return resp;
        }

        private static async Task<HttpResponseData> Error(
            HttpRequestData req, HttpStatusCode status, string message)
        {
            var resp = req.CreateResponse(status);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync(
                JsonSerializer.Serialize(new { success = false, msg = message }));
            return resp;
        }
    }
}
