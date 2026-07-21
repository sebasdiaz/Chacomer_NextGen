using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Proxy HTTP hacia la API oficial de consulta de RUC de la SET Paraguay.
    ///
    /// Endpoint expuesto:
    ///   GET  /api/set/consulta-ruc?ruc={ruc}&amp;dv={dv}
    ///   OPTIONS /api/set/{*any}   (CORS preflight)
    ///
    /// La funcion agrega el apiKey desde la variable de entorno "SetApiKey" y retorna
    /// directamente el JSON de la SET al caller, sin re-serializar.
    /// </summary>
    public class SetConsultaRucFunction
    {
        private readonly SetApiService                     _setApi;
        private readonly ILogger<SetConsultaRucFunction>  _logger;

        public SetConsultaRucFunction(
            SetApiService setApi,
            ILogger<SetConsultaRucFunction> logger)
        {
            _setApi = setApi;
            _logger = logger;
        }

        // ── GET /api/set/consulta-ruc?ruc=XX&dv=Y ───────────────────

        [Function("Set_ConsultaRuc")]
        public async Task<HttpResponseData> ConsultaRuc(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "set/consulta-ruc")]
            HttpRequestData req)
        {
            var qs  = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var ruc = qs["ruc"];
            var dv  = qs["dv"];

            _logger.LogInformation("[Set_ConsultaRuc] ruc={Ruc} dv={Dv}", ruc, dv);

            if (string.IsNullOrWhiteSpace(ruc))
                return await BadRequest(req, "El parametro 'ruc' es requerido.");

            if (string.IsNullOrWhiteSpace(dv))
                return await BadRequest(req, "El parametro 'dv' es requerido.");

            var (json, status) = await _setApi.ConsultaRucAsync(ruc, dv);
            return await BuildResponse(req, json, status);
        }

        // ── OPTIONS /api/set/{*any}  (CORS preflight) ────────────────

        [Function("Set_Options")]
        public async Task<HttpResponseData> Options(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options",
                Route = "set/{*any}")]
            HttpRequestData req)
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(resp);
            return await Task.FromResult(resp);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static async Task<HttpResponseData> BuildResponse(
            HttpRequestData req, string? json, int statusCode)
        {
            if (json == null)
            {
                var errResp = req.CreateResponse((HttpStatusCode)statusCode);
                AddCorsHeaders(errResp);
                errResp.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await errResp.WriteStringAsync("{\"error\":\"Error al conectar con la API de la SET.\"}");
                return errResp;
            }

            var resp = req.CreateResponse((HttpStatusCode)statusCode);
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
