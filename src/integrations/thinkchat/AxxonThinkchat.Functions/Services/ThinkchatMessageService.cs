using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AxxonThinkchat.Functions.Configuration;
using AxxonThinkchat.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>
    /// Envio de plantillas HSM por la API de Thinkchat (accion send_template).
    ///
    /// Mismo endpoint RPC que get_templates: POST contra la URL base con el verbo en
    /// el campo "action". El "from" sale de la configuracion, no del caller.
    ///
    /// La doc del proveedor NO publica el body de respuesta de esta operacion. Lo que
    /// se sabe empiricamente de get_templates es que responde
    /// { "success": bool, "msg": string }, asi que se interpreta "success" cuando esta
    /// y se devuelve igual el body crudo para poder relevar el contrato real.
    /// </summary>
    public class ThinkchatMessageService : IThinkchatMessageService
    {
        private static readonly JsonSerializerOptions BodyOptions = new()
        {
            // Los campos opcionales que no vinieron no se mandan: la API no documenta
            // como trata un null explicito y no vale la pena averiguarlo en produccion.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _httpClient;
        private readonly ThinkchatOptions _options;
        private readonly ILogger<ThinkchatMessageService> _logger;

        public ThinkchatMessageService(
            HttpClient httpClient,
            ThinkchatOptions options,
            ILogger<ThinkchatMessageService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options    = options    ?? throw new ArgumentNullException(nameof(options));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SendTemplateResult> SendTemplateAsync(
            SendTemplateRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var body = new SendTemplatePayload
            {
                Action         = _options.SendTemplateAction,
                From           = _options.From,
                To             = request.To,
                TemplateId     = request.TemplateId,
                // La API espera el array siempre presente, aun para plantillas sin variables.
                TemplateParams = request.TemplateParams ?? new List<string>(),
                TemplateMedia  = request.TemplateMedia ?? string.Empty,
                Extras         = request.Extras
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.TemplatesPath)
            {
                Content = JsonContent.Create(body, options: BodyOptions)
            };
            ThinkchatAuth.Apply(httpRequest, _options);

            _logger.LogInformation(
                "[ThinkchatMessageService] Enviando plantilla. To={To} TemplateId={TemplateId} Params={ParamCount}",
                request.To, request.TemplateId, body.TemplateParams.Count);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            var (success, message) = ReadOutcome(raw);
            var accepted = response.IsSuccessStatusCode && success != false;

            if (accepted)
            {
                _logger.LogInformation(
                    "[ThinkchatMessageService] Plantilla enviada. To={To} TemplateId={TemplateId} " +
                    "HTTP {Status} Body={Body}",
                    request.To, request.TemplateId, (int)response.StatusCode, Truncate(raw, 1000));
            }
            else
            {
                _logger.LogError(
                    "[ThinkchatMessageService] Thinkchat rechazo el envio. To={To} TemplateId={TemplateId} " +
                    "HTTP {Status} Body={Body}",
                    request.To, request.TemplateId, (int)response.StatusCode, Truncate(raw, 2000));
            }

            return new SendTemplateResult(accepted, (int)response.StatusCode, raw, message);
        }

        public async Task<SendTemplateResult> SendTextMessageAsync(
            SendTextMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var body = new SendTextPayload
            {
                Action = _options.SendTextAction,
                From   = _options.From,
                To     = request.To,
                Text   = request.Text
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.TemplatesPath)
            {
                Content = JsonContent.Create(body, options: BodyOptions)
            };
            ThinkchatAuth.Apply(httpRequest, _options);

            _logger.LogInformation(
                "[ThinkchatMessageService] Enviando texto en sesion. To={To} Chars={Chars}",
                request.To, request.Text?.Length ?? 0);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            var (success, message) = ReadOutcome(raw);
            var accepted = response.IsSuccessStatusCode && success != false;

            if (accepted)
            {
                _logger.LogInformation(
                    "[ThinkchatMessageService] Texto enviado. To={To} HTTP {Status} Body={Body}",
                    request.To, (int)response.StatusCode, Truncate(raw, 1000));
            }
            else
            {
                // El rechazo esperable aca es la ventana de 24h cerrada. El proveedor no
                // documenta ese error, asi que el body se loguea entero para relevarlo.
                _logger.LogError(
                    "[ThinkchatMessageService] Thinkchat rechazo el texto. To={To} " +
                    "HTTP {Status} Body={Body}",
                    request.To, (int)response.StatusCode, Truncate(raw, 2000));
            }

            return new SendTemplateResult(accepted, (int)response.StatusCode, raw, message);
        }

        /// <summary>
        /// Lee "success" y "msg" si el body es el JSON habitual del proveedor. Devuelve
        /// success = null cuando no hay forma de saberlo (body vacio, no-JSON o sin el
        /// campo): en ese caso manda el HTTP status, que es lo unico confiable.
        /// </summary>
        private static (bool? Success, string? Message) ReadOutcome(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (null, null);

            try
            {
                using var doc = JsonDocument.Parse(raw);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return (null, null);

                bool? success = doc.RootElement.TryGetProperty("success", out var s)
                    && s.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? s.GetBoolean()
                        : null;

                var message = doc.RootElement.TryGetProperty("msg", out var m)
                    && m.ValueKind == JsonValueKind.String
                        ? m.GetString()
                        : null;

                return (success, message);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "…";

        /// <summary>Body de send_template, con los nombres que espera la API.</summary>
        private sealed class SendTemplatePayload
        {
            [JsonPropertyName("action")]
            public string Action { get; set; } = string.Empty;

            [JsonPropertyName("from")]
            public string From { get; set; } = string.Empty;

            [JsonPropertyName("to")]
            public string? To { get; set; }

            [JsonPropertyName("template_id")]
            public string? TemplateId { get; set; }

            [JsonPropertyName("template_params")]
            public List<string> TemplateParams { get; set; } = new();

            [JsonPropertyName("template_media")]
            public string TemplateMedia { get; set; } = string.Empty;

            [JsonPropertyName("extras")]
            public SendTemplateExtras? Extras { get; set; }
        }

        /// <summary>Body de send_text_msg, con los nombres que espera la API.</summary>
        private sealed class SendTextPayload
        {
            [JsonPropertyName("action")]
            public string Action { get; set; } = string.Empty;

            [JsonPropertyName("from")]
            public string From { get; set; } = string.Empty;

            [JsonPropertyName("to")]
            public string? To { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}
