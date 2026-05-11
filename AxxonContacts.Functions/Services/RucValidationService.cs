using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System.Net.Http.Json;
using System.Text.Json;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Valida el msdyn_identificationnumber contra la API de TURUC
    /// (https://turuc.com.py/api/contribuyente/{id}) y actualiza el contacto con:
    ///   - governmentid  = ruc validado
    ///   - description   = respuesta completa de la API (JSON)
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

        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented        = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly HttpClient          _httpClient;
        private readonly IOrganizationService _service;
        private readonly ILogger             _logger;

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
        // ProcessAsync
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Solo procesa eventos Create sobre contactos raw (no master).
        /// Llama a la API de TURUC y actualiza el contacto si la respuesta es válida.
        /// </summary>
        public async Task ProcessAsync(ContactEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[RucValidationService] Contact={ContactId} | Identification={Identification} | Trigger={Trigger}",
                message.ContactId, message.MsdynIdentificationNumber, message.TriggerMessage);

            // Solo Create
            if (!string.Equals(message.TriggerMessage, "Create", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[RucValidationService] Evento '{Trigger}' ignorado. Solo se procesa Create.",
                    message.TriggerMessage);
                return;
            }

            // El master no necesita validación de RUC (es creado por MasterMatchingService)
            if (message.IsMaster == true)
            {
                _logger.LogInformation("[RucValidationService] Contact es Master. Skip.");
                return;
            }

            // Identificación requerida
            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogWarning("[RucValidationService] IdentificationNumber vacio. Skip.");
                return;
            }

            // Llamar a la API de TURUC
            var (rucData, rawJson) = await CallRucApiAsync(message.MsdynIdentificationNumber);
            if (rucData == null)
            {
                _logger.LogWarning(
                    "[RucValidationService] API no retorno datos validos para '{Identification}'. Skip.",
                    message.MsdynIdentificationNumber);
                return;
            }

            // Actualizar el contacto con los datos validados
            await UpdateContactAsync(message.ContactId, rucData, rawJson);

            _logger.LogInformation(
                "[RucValidationService] Completado. Contact={ContactId} | RUC={Ruc} | Estado={Estado}",
                message.ContactId, rucData.Ruc, rucData.Estado);
        }

        // ────────────────────────────────────────────────────────────
        // CallRucApiAsync
        // ────────────────────────────────────────────────────────────

        private async Task<(RucData? data, string? rawJson)> CallRucApiAsync(string identificationNumber)
        {
            var url = Uri.EscapeDataString(identificationNumber);
            _logger.LogInformation("[RucValidationService] Llamando API TURUC: {Path}", url);

            try
            {
                var response = await _httpClient.GetAsync(url);

                var rawJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "[RucValidationService] API retorno HTTP {Status} para '{Id}'.",
                        (int)response.StatusCode, identificationNumber);
                    return (null, null);
                }

                var apiResponse = JsonSerializer.Deserialize<RucApiResponse>(rawJson, JsonReadOptions);

                if (apiResponse?.Data == null ||
                    !string.Equals(apiResponse.Message, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[RucValidationService] Respuesta invalida de la API. Message='{Msg}'",
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

            try
            {
                await Task.Run(() => _service.Update(upd));
                _logger.LogInformation(
                    "[RucValidationService] Contact {ContactId} actualizado. governmentid={Ruc} axx_fiscalstate={Estado}",
                    contactId, rucData.Ruc, rucData.Estado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[RucValidationService] Error actualizando Contact {ContactId}.",
                    contactId);
                throw;
            }
        }
    }
}
