using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System.Net;

namespace AxxonContacts.Functions.Functions
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
        [OpenApiOperation(operationId: "GetContribuyente", tags: ["Contribuyentes"],
            Summary = "Obtiene un contribuyente por RUC",
            Description = "Consulta la API de TURUC y retorna los datos del contribuyente asociado al RUC indicado.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiParameter(name: "ruc", In = ParameterLocation.Path, Required = true, Type = typeof(string),
            Description = "RUC del contribuyente (ej: 80012345-0).")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(ContribuyenteResponse), Description = "Datos del contribuyente.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
            bodyType: typeof(ContribuyenteResponse), Description = "RUC no encontrado.")]
        public async Task<HttpResponseData> GetContribuyente(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "turuc/contribuyente/{ruc:regex(^\\d.*$)}")]
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
        [OpenApiOperation(operationId: "SearchContribuyentes", tags: ["Contribuyentes"],
            Summary = "Busca contribuyentes por nombre, RUC o documento",
            Description = "Retorna una lista paginada de contribuyentes que coinciden con el texto de búsqueda.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiParameter(name: "search", In = ParameterLocation.Query, Required = true, Type = typeof(string),
            Description = "Nombre, RUC o documento. Mínimo 3 caracteres.")]
        [OpenApiParameter(name: "page", In = ParameterLocation.Query, Required = false, Type = typeof(int),
            Description = "Número de página (base 0). Por defecto: 0.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(SearchContribuyentesResponse), Description = "Lista paginada de contribuyentes.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
            bodyType: typeof(SearchContribuyentesResponse), Description = "Parámetro 'search' inválido.")]
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
        [OpenApiOperation(operationId: "GetContribuyenteTable", tags: ["Contribuyentes"],
            Summary = "Obtiene contribuyentes en formato DataTables",
            Description = "Endpoint compatible con el protocolo server-side de DataTables. Máximo 20 registros por página.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiParameter(name: "draw", In = ParameterLocation.Query, Required = true, Type = typeof(int),
            Description = "Contador de la petición DataTables. Se retorna el mismo valor recibido.")]
        [OpenApiParameter(name: "start", In = ParameterLocation.Query, Required = true, Type = typeof(int),
            Description = "Índice del primer registro (offset).")]
        [OpenApiParameter(name: "length", In = ParameterLocation.Query, Required = true, Type = typeof(int),
            Description = "Cantidad de registros a retornar (máximo 20).")]
        [OpenApiParameter(name: "search", In = ParameterLocation.Query, Required = true, Type = typeof(string),
            Description = "Nombre, RUC o documento. Mínimo 3 caracteres.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(DataTableResponse), Description = "Respuesta DataTables con registros y totales.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
            bodyType: typeof(DataTableResponse), Description = "Parámetro 'search' inválido.")]
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
        [OpenApiOperation(operationId: "GetPersonaJuridica", tags: ["Personas Jurídicas"],
            Summary = "Obtiene datos de persona jurídica por RUC",
            Description = "Retorna información detallada de una persona jurídica registrada en la SET.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiParameter(name: "ruc", In = ParameterLocation.Query, Required = true, Type = typeof(string),
            Description = "RUC de la persona jurídica (ej: 80012345-0).")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(PersonaJuridicaResponse), Description = "Datos de la persona jurídica.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
            bodyType: typeof(PersonaJuridicaResponse), Description = "RUC no encontrado.")]
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
        [OpenApiOperation(operationId: "GetEntidadPublica", tags: ["Entidades Públicas"],
            Summary = "Obtiene datos de entidad pública por RUC",
            Description = "Retorna información detallada de una entidad pública registrada en la SET, incluyendo nivel, dirección y datos de contacto.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiParameter(name: "ruc", In = ParameterLocation.Query, Required = true, Type = typeof(string),
            Description = "RUC de la entidad pública (ej: 80000001-1).")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(EntidadPublicaResponse), Description = "Datos de la entidad pública.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
            bodyType: typeof(EntidadPublicaResponse), Description = "RUC no encontrado.")]
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

        // ── Helpers ──────────────────────────────────────────────────

        private static async Task<HttpResponseData> BuildResponse(
            HttpRequestData req, string? json, int statusCode)
        {
            if (json == null)
            {
                var errResp = req.CreateResponse((HttpStatusCode)statusCode);
                await errResp.WriteStringAsync("{\"error\":\"Error al conectar con la API de TURUC.\"}");
                errResp.Headers.Add("Content-Type", "application/json; charset=utf-8");
                return errResp;
            }

            var resp = req.CreateResponse((HttpStatusCode)statusCode);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync(json);
            return resp;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var resp = req.CreateResponse(HttpStatusCode.BadRequest);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync($"{{\"error\":\"{message}\"}}");
            return resp;
        }
    }
}
