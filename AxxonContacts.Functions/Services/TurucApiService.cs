using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Wrapper del HttpClient para la API publica de TURUC (https://turuc.com.py).
    /// Actua como proxy: reenvía la solicitud y retorna la respuesta cruda (JSON string)
    /// para que las Azure Functions la devuelvan directamente al caller sin re-serializar.
    /// Los errores de red o HTTP no exitosos se registran y retornan como null.
    /// </summary>
    public class TurucApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger    _logger;

        public TurucApiService(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>GET /api/contribuyente/{ruc}</summary>
        public Task<(string? json, int statusCode)> GetContribuyenteAsync(string ruc)
            => GetAsync(Uri.EscapeDataString(ruc));

        /// <summary>GET /api/contribuyente/search?search={search}&amp;page={page}</summary>
        public Task<(string? json, int statusCode)> SearchContribuyentesAsync(string search, int page)
            => GetAsync($"search?search={Uri.EscapeDataString(search)}&page={page}");

        /// <summary>GET /api/contribuyente/table?draw={draw}&amp;start={start}&amp;length={length}&amp;search={search}</summary>
        public Task<(string? json, int statusCode)> GetTableAsync(int draw, int start, int length, string search)
            => GetAsync($"table?draw={draw}&start={start}&length={length}&search={Uri.EscapeDataString(search)}");

        /// <summary>GET /api/contribuyente/persona-juridica?ruc={ruc}</summary>
        public Task<(string? json, int statusCode)> GetPersonaJuridicaAsync(string ruc)
            => GetAsync($"persona-juridica?ruc={Uri.EscapeDataString(ruc)}");

        /// <summary>GET /api/contribuyente/entidad-publica?ruc={ruc}</summary>
        public Task<(string? json, int statusCode)> GetEntidadPublicaAsync(string ruc)
            => GetAsync($"entidad-publica?ruc={Uri.EscapeDataString(ruc)}");

        // ── Implementación común ──────────────────────────────────────

        private async Task<(string? json, int statusCode)> GetAsync(string relativeUrl)
        {
            _logger.LogInformation("[TurucApiService] GET {Url}", relativeUrl);

            try
            {
                var response = await _httpClient.GetAsync(relativeUrl);
                var body     = await response.Content.ReadAsStringAsync();
                var status   = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning(
                        "[TurucApiService] HTTP {Status} para '{Url}'. Body={Body}",
                        status, relativeUrl, body);

                return (body, status);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[TurucApiService] Error de red llamando '{Url}'.", relativeUrl);
                return (null, 502);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[TurucApiService] Timeout llamando '{Url}'.", relativeUrl);
                return (null, 504);
            }
        }
    }
}
