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
    /// Envia una plantilla HSM de WhatsApp por Thinkchat (accion send_template).
    ///
    /// Endpoint expuesto:
    ///   POST /api/thinkchat/send-template   (AuthorizationLevel.Function)
    ///
    /// Body esperado:
    /// <code>
    /// {
    ///   "to": "595981000000",
    ///   "template_id": "29301236-a1c1-45a4-a15c-cdad9207c2e4",
    ///   "template_params": ["Jorge", "NX 350h"],
    ///   "template_media": "",
    ///   "extras": { "inbound_expiration": 60, "inbound_queue": 1 }
    /// }
    /// </code>
    ///
    /// El "from" NO se recibe: sale del App Setting ThinkchatFrom. Que la linea
    /// emisora la elija el caller seria una forma facil de mandar desde una linea
    /// equivocada.
    ///
    /// Antes de enviar se valida el template contra **axx_metatemplates**: que exista,
    /// que este activo y APPROVED, y que la cantidad de template_params coincida con
    /// axx_variables. Un envio con la cantidad equivocada llega al cliente con el texto
    /// roto y ya se cobro: mas vale gastar un Retrieve que un mensaje.
    ///
    /// Respuestas:
    ///   200 — la API acepto el envio. Se devuelve su body crudo.
    ///   400 — el pedido no paso la validacion local (no se llamo a Thinkchat).
    ///   502 — Thinkchat rechazo el envio o fallo. Se devuelve su body crudo para
    ///         no perder el "msg", que es lo unico que explica el motivo.
    ///
    /// El body del proveedor se devuelve tal cual, sin re-serializar: la doc no publica
    /// contrato de respuesta y el caller necesita verlo entero para poder relevarlo.
    /// </summary>
    public class ThinkchatSendTemplateFunction
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IThinkchatMessageService _messageService;
        private readonly IMetatemplateLookup _lookup;
        private readonly ILogger<ThinkchatSendTemplateFunction> _logger;

        public ThinkchatSendTemplateFunction(
            IThinkchatMessageService messageService,
            IMetatemplateLookup lookup,
            ILogger<ThinkchatSendTemplateFunction> logger)
        {
            _messageService = messageService;
            _lookup         = lookup;
            _logger         = logger;
        }

        [Function("Thinkchat_SendTemplate")]
        public async Task<HttpResponseData> SendTemplate(
            [HttpTrigger(AuthorizationLevel.Function, "post",
                Route = "thinkchat/send-template")]
            HttpRequestData req,
            CancellationToken cancellationToken)
        {
            SendTemplateRequest? request;

            try
            {
                request = await JsonSerializer.DeserializeAsync<SendTemplateRequest>(
                    req.Body, JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[Thinkchat_SendTemplate] Body invalido.");
                return await Error(req, HttpStatusCode.BadRequest, "El body no es JSON valido.");
            }

            if (request is null)
                return await Error(req, HttpStatusCode.BadRequest, "El body es requerido.");

            var validation = Validate(request)
                ?? ValidateAgainstMetatemplate(request);

            if (validation is not null)
            {
                _logger.LogWarning(
                    "[Thinkchat_SendTemplate] Pedido rechazado: {Motivo} To={To} TemplateId={TemplateId}",
                    validation, request.To, request.TemplateId);

                return await Error(req, HttpStatusCode.BadRequest, validation);
            }

            var result = await _messageService.SendTemplateAsync(request, cancellationToken);

            // 502 y no el status del proveedor: para el caller esto es una falla del
            // upstream, y propagar un 401 de Thinkchat le haria pensar que su propia
            // llamada quedo sin autorizar.
            var status = result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadGateway;

            return await Passthrough(req, status, result.RawBody);
        }

        /// <summary>Validaciones locales. Devuelve el motivo, o null si el pedido esta bien.</summary>
        private static string? Validate(SendTemplateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.To))
                return "El campo 'to' es requerido.";

            if (string.IsNullOrWhiteSpace(request.TemplateId))
                return "El campo 'template_id' es requerido.";

            // Formato internacional sin "+", tal como lo pide la doc del proveedor.
            if (!request.To.All(char.IsDigit))
                return "El campo 'to' debe ser solo digitos, en formato internacional sin '+'.";

            if (request.Extras?.DestinationCount > 1)
                return "Solo se puede usar un destino de inbound a la vez " +
                       "('inbound_bot', 'inbound_queue' o 'inbound_agent').";

            return null;
        }

        /// <summary>
        /// Valida el pedido contra axx_metatemplates. Devuelve el motivo, o null si esta bien.
        /// </summary>
        private string? ValidateAgainstMetatemplate(SendTemplateRequest request)
        {
            var templateId = request.TemplateId!.Trim();
            var info = _lookup.Lookup(templateId);

            if (!info.Found)
                return $"El template '{templateId}' no existe en axx_metatemplates. " +
                       "La tabla se sincroniza cada 2 horas: si la plantilla es nueva, " +
                       "puede no haber llegado todavia.";

            if (!info.Active)
                return $"El template '{info.Name}' esta inactivo en axx_metatemplates.";

            // ARCHIVED lo rechaza Meta igual, pero del otro lado el error es opaco.
            if (!string.Equals(info.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                return $"El template '{info.Name}' esta en estado {info.Status}, no APPROVED.";

            // Las plantillas con header de media (type image/video) exigen la URL del
            // adjunto: mandarlas con template_media vacio devuelve
            // {"success":false,"msg":"template_media invalido"}. Son 26 de los 77
            // APPROVED de INTE, asi que no es un borde.
            if (info.RequiresMedia && string.IsNullOrWhiteSpace(request.TemplateMedia))
                return $"El template '{info.Name}' es de tipo {info.Type} y necesita " +
                       "'template_media' con la URL publica del adjunto.";

            // null = axx_variables no era un numero; ya se logueo y no se bloquea el envio.
            if (info.Variables is not { } expected)
                return null;

            var received = request.TemplateParams?.Count ?? 0;

            if (received != expected)
                return $"El template '{info.Name}' espera {expected} parametro(s) y " +
                       $"llegaron {received}. 'template_params' es posicional: la cantidad " +
                       "y el orden tienen que coincidir con la plantilla aprobada por Meta.";

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
