using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System.Net.Http.Json;
using System.Text.Json;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Valida un msdyn_identificationnumber contra la API de TURUC
    /// (https://turuc.com.py/api/contribuyente/{id}) y actualiza el contacto master con:
    ///   - governmentid    = ruc validado (ej: "80012345-0")
    ///   - description     = respuesta completa de la API (JSON)
    ///   - axx_fiscalstate = estado mapeado a OptionSet
    /// </summary>
    public class RucValidationService
    {
        private const string EntityLogicalName = "contact";

        // Mapeo estado API → valor OptionSet axx_fiscalstate
        private static readonly Dictionary<string, int> EstadoMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Activo",     1 },
                { "Suspendido", 2 },
                { "Cancelado",  3 },
                { "Bloqueado",  4 },
                { "No Vigente", 5 }
            };

        private static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient           _httpClient;
        private readonly IOrganizationService _service;
        private readonly ILogger              _logger;

        public RucValidationService(
            HttpClient httpClient,
            IOrganizationService service,
            ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _service    = service   ?? throw new ArgumentNullException(nameof(service));
            _logger     = logger    ?? throw new ArgumentNullException(nameof(logger));
        }

        // ────────────────────────────────────────────────────────────
        // ValidateAndUpdateAsync
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Llama a la API de TURUC para el <paramref name="identificationNumber"/> dado
        /// y actualiza el contacto master (<paramref name="masterId"/>) con los datos validados.
        /// No lanza excepciones por errores de la API; los registra como Warning y retorna.
        /// </summary>
        public async Task ValidateAndUpdateAsync(Guid masterId, string identificationNumber)
        {
            if (string.IsNullOrWhiteSpace(identificationNumber))
            {
                _logger.LogWarning("[RucValidationService] IdentificationNumber vacio. Skip.");
                return;
            }

            _logger.LogInformation(
                "[RucValidationService] Validando RUC '{Identification}' → Master {MasterId}",
                identificationNumber, masterId);

            var (rucData, rawJson) = await CallRucApiAsync(identificationNumber);

            if (rucData == null)
            {
                _logger.LogWarning(
                    "[RucValidationService] API no retorno datos validos para '{Identification}'. Skip.",
                    identificationNumber);
                return;
            }

            await UpdateContactAsync(masterId, rucData, rawJson);

            _logger.LogInformation(
                "[RucValidationService] Master {MasterId} actualizado. RUC={Ruc} | Estado={Estado}",
                masterId, rucData.Ruc, rucData.Estado);
        }

        // ────────────────────────────────────────────────────────────
        // CallRucApiAsync
        // ────────────────────────────────────────────────────────────

        private async Task<(RucData? data, string? rawJson)> CallRucApiAsync(string identificationNumber)
        {
            var path = Uri.EscapeDataString(identificationNumber);
            _logger.LogInformation("[RucValidationService] GET {Path}", path);

            try
            {
                var response = await _httpClient.GetAsync(path);
                var rawJson  = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "[RucValidationService] API retorno HTTP {Status} para '{Id}'. Body={Body}",
                        (int)response.StatusCode, identificationNumber, rawJson);
                    return (null, null);
                }

                var apiResponse = JsonSerializer.Deserialize<RucApiResponse>(rawJson, JsonReadOptions);

                if (apiResponse?.Data == null ||
                    !string.Equals(apiResponse.Message, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[RucValidationService] Respuesta invalida. Message='{Msg}'",
                        apiResponse?.Message);
                    return (null, null);
                }

                return (apiResponse.Data, rawJson);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "[RucValidationService] Error de red llamando API TURUC.");
                return (null, null);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[RucValidationService] Error deserializando respuesta de API TURUC.");
                return (null, null);
            }
        }

        // ────────────────────────────────────────────────────────────
        // UpdateContactAsync
        // ────────────────────────────────────────────────────────────

        private async Task UpdateContactAsync(Guid contactId, RucData rucData, string? rawJson)
        {
            var upd = new Entity(EntityLogicalName, contactId);

            // governmentid = ruc validado (ej: "80012345-0")
            if (!string.IsNullOrEmpty(rucData.Ruc))
                upd["governmentid"] = rucData.Ruc;

            // description = respuesta completa de la API
            if (!string.IsNullOrEmpty(rawJson))
                upd["description"] = rawJson;

            // axx_fiscalstate = mapeo de estado a OptionSet
            if (!string.IsNullOrEmpty(rucData.Estado))
            {
                if (EstadoMap.TryGetValue(rucData.Estado, out var estadoValue))
                    upd["axx_fiscalstate"] = new OptionSetValue(estadoValue);
                else
                    _logger.LogWarning(
                        "[RucValidationService] Estado '{Estado}' no reconocido. axx_fiscalstate no actualizado.",
                        rucData.Estado);
            }

            await Task.Run(() => _service.Update(upd));
        }
    }
}
