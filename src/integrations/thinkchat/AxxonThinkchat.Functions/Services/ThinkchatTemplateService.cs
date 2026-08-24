using System.Net.Http.Json;
using System.Text.Json;
using AxxonThinkchat.Functions.Configuration;
using AxxonThinkchat.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>
    /// Cliente HTTP de la API de Thinkchat. Mismo patron que SetApiService/TurucApiService
    /// de Axxon.Eip.Core: HttpClient nombrado via IHttpClientFactory y credencial
    /// resuelta por EipSecretResolver.
    ///
    /// La API es RPC sobre un endpoint unico: POST contra la URL base con el verbo
    /// logico en el body — { "action": "get_templates", "from": "<ThinkchatFrom>" } —
    /// y el token en Authorization: Bearer. NO hay una ruta /get_templates: pedirla
    /// devuelve el 404 de nginx.
    ///
    /// El response es { "success": true, "templates": [ ... ] }.
    /// La doc del proveedor no documenta paginacion: se asume una sola llamada.
    /// </summary>
    public class ThinkchatTemplateService : IThinkchatTemplateService
    {
        /// <summary>Nombre del HttpClient nombrado en el IHttpClientFactory.</summary>
        public const string HttpClientName = "ThinkchatApi";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly ThinkchatOptions _options;
        private readonly ILogger<ThinkchatTemplateService> _logger;

        public ThinkchatTemplateService(
            HttpClient httpClient,
            ThinkchatOptions options,
            ILogger<ThinkchatTemplateService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options    = options    ?? throw new ArgumentNullException(nameof(options));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<ThinkchatTemplate>> GetTemplatesAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                _logger.LogWarning(
                    "[ThinkchatTemplateService] No se resolvio el token (secret 'secretThinkChat').");

            if (string.IsNullOrWhiteSpace(_options.From))
                _logger.LogWarning(
                    "[ThinkchatTemplateService] ThinkchatFrom no esta configurado: " +
                    "el body va sin numero emisor y la API probablemente rechace el request.");

            // TemplatesPath vacio => la request va contra la BaseAddress, que es el
            // endpoint RPC. HttpRequestMessage trata la cadena vacia como null y
            // HttpClient resuelve la BaseAddress sola.
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TemplatesPath)
            {
                Content = JsonContent.Create(new { action = _options.Action, from = _options.From })
            };
            ThinkchatAuth.Apply(request, _options);

            _logger.LogInformation(
                "[ThinkchatTemplateService] POST {BaseUrl}{Path} action={Action} from={From}",
                _httpClient.BaseAddress, _options.TemplatesPath, _options.Action, _options.From);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[ThinkchatTemplateService] HTTP {Status} al leer templates. Body={Body}",
                    (int)response.StatusCode, Truncate(body, 2000));

                throw new HttpRequestException(
                    $"Thinkchat devolvio HTTP {(int)response.StatusCode} al leer templates.");
            }

            var templates = Deserialize(body);

            _logger.LogInformation(
                "[ThinkchatTemplateService] {Count} templates leidos.", templates.Count);

            return templates;
        }

        /// <summary>
        /// La forma real es { "success": true, "templates": [ ... ] }. Se aceptan igual
        /// el array en la raiz y los otros envoltorios habituales por si el proveedor
        /// cambia: no cuesta nada y evita otra corrida perdida.
        /// </summary>
        private List<ThinkchatTemplate> Deserialize(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return Materialize(root);

            if (root.ValueKind == JsonValueKind.Object)
            {
                // La API puede responder HTTP 200 con success=false (ej. accion o linea
                // invalida). Sin este chequeo el error saldria como "response con forma
                // inesperada", que manda a buscar el problema al lugar equivocado.
                if (root.TryGetProperty("success", out var success)
                    && success.ValueKind == JsonValueKind.False)
                {
                    var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : null;

                    throw new InvalidOperationException(
                        $"Thinkchat rechazo el pedido de templates: {msg ?? "sin mensaje"}.");
                }

                foreach (var name in new[] { "templates", "data", "result" })
                {
                    if (root.TryGetProperty(name, out var wrapped)
                        && wrapped.ValueKind == JsonValueKind.Array)
                        return Materialize(wrapped);
                }
            }

            _logger.LogError(
                "[ThinkchatTemplateService] Response con forma inesperada (ValueKind={Kind}). Body={Body}",
                root.ValueKind, Truncate(body, 2000));

            throw new InvalidOperationException(
                "El response de get_template no tiene un array de templates reconocible.");
        }

        private static List<ThinkchatTemplate> Materialize(JsonElement array) =>
            array.Deserialize<List<ThinkchatTemplate>>(JsonOptions) ?? new List<ThinkchatTemplate>();

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "…";
    }
}
