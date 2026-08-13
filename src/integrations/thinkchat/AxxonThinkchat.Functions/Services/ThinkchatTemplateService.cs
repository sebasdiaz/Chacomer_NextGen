using System.Net.Http.Headers;
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
    /// get_template lleva parametros en el body, asi que se invoca por POST con
    /// { "from": "<ThinkchatFrom>" }.
    ///
    /// PENDIENTE DE CONFIRMAR contra la collection de Postman:
    ///   - esquema de auth (default Authorization: Bearer)
    ///   - si el body lleva algun campo mas ademas de "from"
    ///   - si el response es un array en la raiz o viene envuelto en un objeto
    ///   - si pagina (hoy se asume que devuelve todos los templates en una llamada)
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

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TemplatesPath)
            {
                Content = JsonContent.Create(new { from = _options.From })
            };
            ApplyAuth(request);

            _logger.LogInformation(
                "[ThinkchatTemplateService] POST {BaseUrl}{Path} from={From}",
                _httpClient.BaseAddress, _options.TemplatesPath, _options.From);

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

        private void ApplyAuth(HttpRequestMessage request)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                return;

            if (_options.AuthHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_options.AuthScheme))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
                return;
            }

            request.Headers.TryAddWithoutValidation(_options.AuthHeader, _options.ApiKey);
        }

        /// <summary>
        /// Acepta tanto un array de templates en la raiz como un objeto que los envuelve
        /// en "data" / "templates" / "result", que son las tres formas habituales.
        /// Si la collection muestra otra, se ajusta aca.
        /// </summary>
        private List<ThinkchatTemplate> Deserialize(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return Materialize(root);

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "data", "templates", "result" })
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
