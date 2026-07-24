using Axxon.Eip.Core.Fiscal;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Valida un msdyn_identificationnumber (formato "XXXXXXXX-DV") contra la API oficial
    /// de la SET Paraguay y actualiza el registro master con:
    ///   - axx_dnitresponse = JSON crudo de la respuesta (siempre sobreescrito)
    ///   - axx_fiscalstate  = contribuyente.estado mapeado al OptionSet
    ///
    /// Funciona para account y contact pasando el entityLogicalName correspondiente.
    /// No lanza excepciones por errores de la API; los registra como Warning.
    /// </summary>
    public class SetRucValidationService
    {
        // Mapeo estado SET → valor OptionSet axx_fiscalstate (mismo en account y contact)
        private static readonly Dictionary<string, int> EstadoMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "ACTIVO",              1 },
                { "SUSPENDIDO",          2 },
                { "CANCELADO",           3 },
                { "BLOQUEADO",           4 },
                { "NO VIGENTE",          5 },
                { "SUSPENSION TEMPORAL", 6 }
            };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly SetApiService        _setApi;
        private readonly IOrganizationService _service;
        private readonly ILogger              _logger;

        public SetRucValidationService(
            SetApiService        setApi,
            IOrganizationService service,
            ILogger              logger)
        {
            _setApi  = setApi   ?? throw new ArgumentNullException(nameof(setApi));
            _service = service  ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger   ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Consulta la SET y actualiza axx_dnitresponse + axx_fiscalstate en el master.
        /// </summary>
        /// <param name="entityLogicalName">"account" o "contact"</param>
        /// <param name="masterId">Id del registro master a actualizar</param>
        /// <param name="identificationNumber">Formato esperado: "80054203-7"</param>
        public async Task ValidateAndUpdateAsync(
            string entityLogicalName,
            Guid   masterId,
            string identificationNumber)
        {
            if (string.IsNullOrWhiteSpace(identificationNumber))
            {
                _logger.LogWarning("[SetRucValidationService] IdentificationNumber vacio. Skip.");
                return;
            }

            // Parsear RUC y DV desde "80054203-7"
            var dashIdx = identificationNumber.IndexOf('-');
            if (dashIdx <= 0)
            {
                _logger.LogWarning(
                    "[SetRucValidationService] Formato invalido '{Id}': no contiene digito verificador (esperado XXXXXXXX-DV).",
                    identificationNumber);
                return;
            }

            var ruc = identificationNumber[..dashIdx].Trim();
            var dv  = identificationNumber[(dashIdx + 1)..].Trim();

            _logger.LogInformation(
                "[SetRucValidationService] Consultando SET. Entity={Entity} | Master={MasterId} | RUC={Ruc} | DV={Dv}",
                entityLogicalName, masterId, ruc, dv);

            var (json, statusCode) = await _setApi.ConsultaRucAsync(ruc, dv);

            await UpdateMasterAsync(entityLogicalName, masterId, json, identificationNumber);
        }

        // ────────────────────────────────────────────────────────────

        private async Task UpdateMasterAsync(
            string entityLogicalName,
            Guid   masterId,
            string? rawJson,
            string  identificationNumber)
        {
            var upd = new Entity(entityLogicalName, masterId);

            // axx_dnitresponse: siempre sobreescribir con el ultimo response
            upd["axx_dnitresponse"] = rawJson ?? string.Empty;

            // Parsear contribuyente.estado y mapear a axx_fiscalstate
            if (!string.IsNullOrEmpty(rawJson))
            {
                try
                {
                    var response = JsonSerializer.Deserialize<SetRucResponse>(rawJson, JsonOpts);
                    var estado   = response?.Contribuyente?.Estado;

                    if (!string.IsNullOrEmpty(estado))
                    {
                        if (EstadoMap.TryGetValue(estado, out var estadoValue))
                        {
                            // contact: axx_fiscalstate es multi-select (OptionSetValueCollection)
                            // account: axx_fiscalstate es single-select (OptionSetValue)
                            if (entityLogicalName == "contact")
                                upd["axx_fiscalstate"] = new OptionSetValueCollection(new List<OptionSetValue> { new OptionSetValue(estadoValue) });
                            else
                                upd["axx_fiscalstate"] = new OptionSetValue(estadoValue);

                            _logger.LogInformation(
                                "[SetRucValidationService] Master {MasterId} | Estado={Estado} → axx_fiscalstate={Value}",
                                masterId, estado, estadoValue);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[SetRucValidationService] Estado '{Estado}' no reconocido. axx_fiscalstate no actualizado.",
                                estado);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "[SetRucValidationService] No se pudo parsear respuesta SET para '{Id}'.",
                        identificationNumber);
                }
            }

            try
            {
                await Task.Run(() => _service.Update(upd));
                _logger.LogInformation(
                    "[SetRucValidationService] Master {MasterId} actualizado ({Entity}).",
                    masterId, entityLogicalName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SetRucValidationService] Error actualizando Master {MasterId}.",
                    masterId);
            }
        }

        // ── Modelos de respuesta SET ──────────────────────────────────

        private sealed class SetRucResponse
        {
            [JsonPropertyName("codigo")]
            public string? Codigo { get; set; }

            [JsonPropertyName("contribuyente")]
            public SetContribuyente? Contribuyente { get; set; }
        }

        private sealed class SetContribuyente
        {
            [JsonPropertyName("estado")]
            public string? Estado { get; set; }

            [JsonPropertyName("razonSocial")]
            public string? RazonSocial { get; set; }
        }
    }
}
