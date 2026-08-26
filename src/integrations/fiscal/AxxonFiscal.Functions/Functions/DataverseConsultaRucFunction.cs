using AxxonFiscal.Functions.Models;
using AxxonFiscal.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace AxxonFiscal.Functions.Functions
{
    /// <summary>
    /// Consulta de partes (contact y account) en Dataverse por RUC.
    ///
    /// Endpoints expuestos:
    ///   GET     /api/dataverse/consulta-ruc?ruc={ruc}
    ///   OPTIONS /api/dataverse/{*any}   (CORS preflight)
    ///
    /// A diferencia de los otros endpoints de esta app, el origen no es una API externa
    /// sino Dataverse: devuelve nombre, identification number y tipo de persona de cada
    /// contact/account cuyo msdyn_identificationnumber coincide con el RUC.
    ///
    /// Es de solo lectura. El tipo de persona se deriva de la tabla (account →
    /// "Juridica", contact → "Fisica"), no de un campo.
    /// </summary>
    public class DataverseConsultaRucFunction
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Los nombres ya vienen fijados por [JsonPropertyName] en los DTOs.
            WriteIndented = false
        };

        private readonly PartyLookupService _partyLookup;
        private readonly ILogger<DataverseConsultaRucFunction> _logger;

        public DataverseConsultaRucFunction(
            PartyLookupService partyLookup,
            ILogger<DataverseConsultaRucFunction> logger)
        {
            _partyLookup = partyLookup;
            _logger      = logger;
        }

        // ── GET /api/dataverse/consulta-ruc?ruc=XX ───────────────────

        [Function("Dataverse_ConsultaRuc")]
        public async Task<HttpResponseData> ConsultaRuc(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "dataverse/consulta-ruc")]
            HttpRequestData req)
        {
            var ruc = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ruc"];

            _logger.LogInformation("[Dataverse_ConsultaRuc] ruc={Ruc}", ruc);

            if (string.IsNullOrWhiteSpace(ruc))
                return await BadRequest(req, "El parametro 'ruc' es requerido.");

            try
            {
                var result = await _partyLookup.FindByRucAsync(ruc);
                return await Ok(req, JsonSerializer.Serialize(result, JsonOpts));
            }
            catch (Exception ex)
            {
                // Dataverse caido, MI sin Application User, o RUC que rompe la query.
                // Se loguea con el detalle y al caller le vuelve un 502 sin internals.
                _logger.LogError(ex, "[Dataverse_ConsultaRuc] Error consultando Dataverse. ruc={Ruc}", ruc);

                var resp = req.CreateResponse(HttpStatusCode.BadGateway);
                AddCorsHeaders(resp);
                resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await resp.WriteStringAsync("{\"error\":\"Error al consultar Dataverse.\"}");
                return resp;
            }
        }

        // ── OPTIONS /api/dataverse/{*any}  (CORS preflight) ──────────

        [Function("Dataverse_Options")]
        public async Task<HttpResponseData> Options(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options",
                Route = "dataverse/{*any}")]
            HttpRequestData req)
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(resp);
            return await Task.FromResult(resp);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static async Task<HttpResponseData> Ok(HttpRequestData req, string json)
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(resp);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync(json);
            return resp;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var resp = req.CreateResponse(HttpStatusCode.BadRequest);
            AddCorsHeaders(resp);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync($"{{\"error\":\"{message}\"}}");
            return resp;
        }

        private static void AddCorsHeaders(HttpResponseData resp)
        {
            resp.Headers.Add("Access-Control-Allow-Origin",  "*");
            resp.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
        }
    }
}
