using Axxon.Eip.Core.Fiscal;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AxxonFiscal.Functions.Functions
{
    /// <summary>
    /// Proxy HTTP de la API publica de TURUC (https://turuc.com.py/api/contribuyente).
    /// Expone los endpoints como Azure Functions con AuthorizationLevel.Function (API key).
    /// Retorna directamente el JSON de TURUC sin re-serializar.
    ///
    /// Endpoints disponibles:
    ///   GET /api/turuc/contribuyente/{ruc}
    ///   GET /api/turuc/contribuyente/search?search={texto}&amp;page={n}
    ///   GET /api/turuc/contribuyente/table?draw={n}&amp;start={n}&amp;length={n}&amp;search={texto}
    ///   GET /api/turuc/persona-juridica?ruc={ruc}
    ///   GET /api/turuc/entidad-publica?ruc={ruc}
    /// </summary>
    public class TurucContribuyentesFunction
    {
        private readonly TurucApiService _turucApi;
        private readonly ILogger<TurucContribuyentesFunction> _logger;

        public TurucContribuyentesFunction(
            TurucApiService turucApi,
            ILogger<TurucContribuyentesFunction> logger)
        {
            _turucApi = turucApi;
            _logger   = logger;
        }

        // ── GET /api/turuc/contribuyente/{ruc} ───────────────────────

        [Function("Turuc_GetContribuyente")]
        public async Task<HttpResponseData> GetContribuyente(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/contribuyente/{ruc}")]
            HttpRequestData req,
            string ruc)
        {
            _logger.LogInformation("[Turuc_GetContribuyente] ruc={Ruc}", ruc);

            if (string.IsNullOrWhiteSpace(ruc))
                return await BadRequest(req, "El parametro 'ruc' es requerido.");

            var (json, status) = await _turucApi.GetContribuyenteAsync(ruc);
            return await BuildResponse(req, json, status);
        }

        // ── GET /api/turuc/contribuyente/search ──────────────────────

        [Function("Turuc_SearchContribuyentes")]
        public async Task<HttpResponseData> SearchContribuyentes(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/contribuyente/search")]
            HttpRequestData req)
        {
            var qs     = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var search = qs["search"];
            var pageStr = qs["page"];

            _logger.LogInformation("[Turuc_SearchContribuyentes] search={Search} page={Page}", search, pageStr);

            if (string.IsNullOrWhiteSpace(search) || search.Length < 3)
                return await BadRequest(req, "El parametro 'search' es requerido y debe tener al menos 3 caracteres.");

            if (!int.TryParse(pageStr, out var page) || page < 0)
                page = 0;

            var (json, status) = await _turucApi.SearchContribuyentesAsync(search, page);
            return await BuildResponse(req, json, status);
        }

        // ── GET /api/turuc/contribuyente/table ───────────────────────

        [Function("Turuc_GetContribuyenteTable")]
        public async Task<HttpResponseData> GetContribuyenteTable(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/contribuyente/table")]
            HttpRequestData req)
        {
            var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var search = qs["search"];

            int.TryParse(qs["draw"],   out var draw);
            int.TryParse(qs["start"],  out var start);
            int.TryParse(qs["length"], out var length);
            if (length <= 0 || length > 20) length = 20;

            _logger.LogInformation(
                "[Turuc_GetContribuyenteTable] draw={Draw} start={Start} length={Length} search={Search}",
                draw, start, length, search);

            if (string.IsNullOrWhiteSpace(search) || search.Length < 3)
                return await BadRequest(req, "El parametro 'search' es requerido y debe tener al menos 3 caracteres.");

            var (json, status) = await _turucApi.GetTableAsync(draw, start, length, search);
            return await BuildResponse(req, json, status);
        }

        // ── GET /api/turuc/persona-juridica ──────────────────────────

        [Function("Turuc_GetPersonaJuridica")]
        public async Task<HttpResponseData> GetPersonaJuridica(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/persona-juridica")]
            HttpRequestData req)
        {
            var ruc = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ruc"];

            _logger.LogInformation("[Turuc_GetPersonaJuridica] ruc={Ruc}", ruc);

            if (string.IsNullOrWhiteSpace(ruc))
                return await BadRequest(req, "El parametro 'ruc' es requerido.");

            var (json, status) = await _turucApi.GetPersonaJuridicaAsync(ruc);
            return await BuildResponse(req, json, status);
        }

        // ── GET /api/turuc/entidad-publica ───────────────────────────

        [Function("Turuc_GetEntidadPublica")]
        public async Task<HttpResponseData> GetEntidadPublica(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/entidad-publica")]
            HttpRequestData req)
        {
            var ruc = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ruc"];

            _logger.LogInformation("[Turuc_GetEntidadPublica] ruc={Ruc}", ruc);

            if (string.IsNullOrWhiteSpace(ruc))
                return await BadRequest(req, "El parametro 'ruc' es requerido.");

            var (json, status) = await _turucApi.GetEntidadPublicaAsync(ruc);
            return await BuildResponse(req, json, status);
        }

        // ── OPTIONS preflight (CORS) ─────────────────────────────────

        [Function("Turuc_Options")]
        public async Task<HttpResponseData> Options(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "turuc/{*any}")]
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
                errResp.Headers.Add("Content-Type", "application/json; charset=utf-8");
                AddCorsHeaders(errResp);
                await errResp.WriteStringAsync("{\"error\":\"Error al conectar con la API de TURUC.\"}");
                return errResp;
            }

            var resp = req.CreateResponse((HttpStatusCode)statusCode);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            AddCorsHeaders(resp);
            await resp.WriteStringAsync(json);
            return resp;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var resp = req.CreateResponse(HttpStatusCode.BadRequest);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            AddCorsHeaders(resp);
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
