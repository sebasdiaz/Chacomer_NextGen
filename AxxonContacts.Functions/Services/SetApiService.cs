using AxxonContacts.Functions.Configuration;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Cliente HTTP para la API oficial de consulta de RUC de la SET Paraguay.
    /// Endpoint: https://servicios.set.gov.py/EsetApiWS/ApiWS/consultaRuc?apiKey=&amp;ruc=&amp;dv=
    ///
    /// El API Key se obtiene de la variable de entorno "SetApiKey" (AppSettings).
    /// La respuesta cruda de la SET se retorna sin re-serializar para que la Azure Function
    /// la devuelva directamente al caller.
    /// </summary>
    public class SetApiService
    {
        private readonly HttpClient  _httpClient;
        private readonly AppSettings _settings;
        private readonly ILogger     _logger;

        public SetApiService(HttpClient httpClient, AppSettings settings, ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings   = settings   ?? throw new ArgumentNullException(nameof(settings));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Consulta el RUC en la API oficial de la SET.
        /// </summary>
        /// <param name="ruc">Numero de documento sin el digito verificador (ej: "2257151").</param>
        /// <param name="dv">Digito verificador (ej: "5").</param>
        /// <returns>
        /// Tupla con el JSON crudo de la respuesta y el codigo HTTP.
        /// Si ocurre un error de red retorna (null, 502/504).
        /// </returns>
        public Task<(string? json, int statusCode)> ConsultaRucAsync(string ruc, string dv)
        {
            var apiKey = _settings.SetApiKey ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("[SetApiService] SetApiKey no esta configurado.");

            var relativeUrl =
                $"consultaRuc?apiKey={Uri.EscapeDataString(apiKey)}" +
                $"&ruc={Uri.EscapeDataString(ruc)}" +
                $"&dv={Uri.EscapeDataString(dv)}";

            return GetAsync(relativeUrl);
        }

        // ── Implementacion comun ──────────────────────────────────────

        private async Task<(string? json, int statusCode)> GetAsync(string relativeUrl)
        {
            // Log con apiKey enmascarada para no exponer en logs
            var logUrl = System.Text.RegularExpressions.Regex.Replace(
                relativeUrl, @"apiKey=[^&]*", "apiKey=***");
            _logger.LogInformation("[SetApiService] GET {Url}", logUrl);

            try
            {
                var response = await _httpClient.GetAsync(relativeUrl);
                var body     = await response.Content.ReadAsStringAsync();
                var status   = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning(
                        "[SetApiService] HTTP {Status} para '{Url}'. Body={Body}",
                        status, logUrl, body);

                return (body, status);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SetApiService] Error de red llamando '{Url}'.", logUrl);
                return (null, 502);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[SetApiService] Timeout llamando '{Url}'.", logUrl);
                return (null, 504);
            }
        }
    }
}
