using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Fiscal
{
    /// <summary>
    /// Cliente HTTP para los servicios de Consulta Pública de la SET Paraguay (DNIT).
    /// Base: https://servicios.set.gov.py/EsetApiWS/ApiWS/
    ///
    /// Métodos disponibles:
    ///   ConsultaRucAsync                          → consultaRuc
    ///   ValidezDocumentoTimbradoAsync             → validezDocumentoTimbrado
    ///   ValidezDocumentoMaquinaRegistradoraAsync  → validezDocumentoMaquinaRegistradora
    ///
    /// El API Key se obtiene de <see cref="SetApiOptions.ApiKey"/> (config "SetApiKey").
    /// La respuesta cruda de la SET se retorna sin re-serializar para que el caller
    /// (Azure Function o servicio de validación) la use directamente.
    ///
    /// Componente cross de la EiP: lo consumen tanto las Azure Functions HTTP fiscales
    /// (AxxonFiscal.Functions) como el path de matching por Service Bus (AxxonContacts).
    /// </summary>
    public class SetApiService
    {
        /// <summary>Nombre del HttpClient nombrado en el IHttpClientFactory.</summary>
        public const string HttpClientName = "SetApi";

        private readonly HttpClient   _httpClient;
        private readonly SetApiOptions _options;
        private readonly ILogger      _logger;

        public SetApiService(HttpClient httpClient, SetApiOptions options, ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options    = options    ?? throw new ArgumentNullException(nameof(options));
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
            var apiKey = _options.ApiKey ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("[SetApiService] SetApiKey no esta configurado.");

            var relativeUrl =
                $"consultaRuc?apiKey={Uri.EscapeDataString(apiKey)}" +
                $"&ruc={Uri.EscapeDataString(ruc)}" +
                $"&dv={Uri.EscapeDataString(dv)}";

            return GetAsync(relativeUrl);
        }

        /// <summary>
        /// Verifica la validez de un Documento Timbrado en la API oficial de la SET.
        /// Spec: sección 2.3 — Servicio Web Validez Documento Timbrado.
        /// </summary>
        /// <param name="ruc">RUC del emisor sin DV (ej: "2257151").</param>
        /// <param name="dv">Dígito verificador (ej: "5").</param>
        /// <param name="numeroTimbrado">Número de timbrado asignado por la DNIT.</param>
        /// <param name="tipoDocumento">
        ///   ID del tipo de documento (ej: "1"=FACTURA, "2"=BOLETA DE VENTA,
        ///   "60"=FACTURA ELECTRONICA, etc.). Ver tabla en spec §2.3.
        /// </param>
        /// <param name="numeroDocumento">
        ///   Número del comprobante, formato nnn-nnn-nnnnnnn (ej: "001-002-0000123").
        /// </param>
        /// <param name="fechaExpedicion">Fecha de emisión, formato DD/MM/AAAA (ej: "15/05/2024").</param>
        /// <param name="medioGeneracion">
        ///   ID del medio de generación ("1"=AUTOIMPRESORES, "3"=PREIMPRESOS,
        ///   "6"=COMPROBANTES VIRTUALES, "7"=DOCUMENTOS ELECTRÓNICOS).
        /// </param>
        public Task<(string? json, int statusCode)> ValidezDocumentoTimbradoAsync(
            string ruc,
            string dv,
            string numeroTimbrado,
            string tipoDocumento,
            string numeroDocumento,
            string fechaExpedicion,
            string medioGeneracion)
        {
            var apiKey = _options.ApiKey ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("[SetApiService] SetApiKey no esta configurado.");

            var relativeUrl =
                $"validezDocumentoTimbrado?apiKey={Uri.EscapeDataString(apiKey)}" +
                $"&ruc={Uri.EscapeDataString(ruc)}" +
                $"&dv={Uri.EscapeDataString(dv)}" +
                $"&numero_timbrado={Uri.EscapeDataString(numeroTimbrado)}" +
                $"&tipo_documento={Uri.EscapeDataString(tipoDocumento)}" +
                $"&numero_documento={Uri.EscapeDataString(numeroDocumento)}" +
                $"&fecha_expedicion={Uri.EscapeDataString(fechaExpedicion)}" +
                $"&medio_generacion={Uri.EscapeDataString(medioGeneracion)}";

            return GetAsync(relativeUrl);
        }

        /// <summary>
        /// Verifica la validez del timbrado de un documento emitido por una Máquina Registradora.
        /// Spec: sección 2.4 — Servicio Web Validez Documento Máquina Registradora.
        /// </summary>
        /// <param name="ruc">RUC del emisor sin DV (ej: "2257151").</param>
        /// <param name="dv">Dígito verificador (ej: "5").</param>
        /// <param name="numeroTimbrado">Número de timbrado asignado por la DNIT.</param>
        /// <param name="fechaExpedicion">Fecha de emisión, formato DD/MM/AAAA (ej: "15/05/2024").</param>
        /// <param name="medioGeneracion">
        ///   ID del medio de generación. Para máquinas registradoras siempre es "2".
        /// </param>
        public Task<(string? json, int statusCode)> ValidezDocumentoMaquinaRegistradoraAsync(
            string ruc,
            string dv,
            string numeroTimbrado,
            string fechaExpedicion,
            string medioGeneracion)
        {
            var apiKey = _options.ApiKey ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("[SetApiService] SetApiKey no esta configurado.");

            var relativeUrl =
                $"validezDocumentoMaquinaRegistradora?apiKey={Uri.EscapeDataString(apiKey)}" +
                $"&ruc={Uri.EscapeDataString(ruc)}" +
                $"&dv={Uri.EscapeDataString(dv)}" +
                $"&numero_timbrado={Uri.EscapeDataString(numeroTimbrado)}" +
                $"&fecha_expedicion={Uri.EscapeDataString(fechaExpedicion)}" +
                $"&medio_generacion={Uri.EscapeDataString(medioGeneracion)}";

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
