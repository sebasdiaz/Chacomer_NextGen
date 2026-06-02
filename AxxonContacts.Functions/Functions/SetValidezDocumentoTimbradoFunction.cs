using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Proxy HTTP hacia el servicio oficial de Validez de Documento Timbrado de la SET Paraguay.
    /// Especificación técnica: §2.3 — Servicio Web Validez Documento Timbrado (DNIT, enero/2024).
    ///
    /// Endpoint expuesto:
    ///   GET  /api/set/validez-documento-timbrado
    ///       ?ruc=valorRUC
    ///       &amp;dv=valorDV
    ///       &amp;numero_timbrado=valorNumeroTimbrado
    ///       &amp;tipo_documento=valorTipoDocumento
    ///       &amp;numero_documento=nnn-nnn-nnnnnnn
    ///       &amp;fecha_expedicion=DD/MM/AAAA
    ///       &amp;medio_generacion=valorMedioGeneracion
    ///
    ///   OPTIONS /api/set/{*any}   (CORS preflight — manejado por SetConsultaRucFunction)
    ///
    /// La función inyecta el apiKey desde la variable de entorno "SetApiKey" y retorna
    /// directamente el JSON de la SET al caller sin re-serializar.
    ///
    /// Respuesta SET:
    ///   Válido   → { "mensaje": "VALIDO",   "estado": "VALIDO"   }
    ///   Inválido → { "mensaje": "..motivo..", "estado": "INVALIDO" }
    /// </summary>
    public class SetValidezDocumentoTimbradoFunction
    {
        private readonly SetApiService                                  _setApi;
        private readonly ILogger<SetValidezDocumentoTimbradoFunction>  _logger;

        public SetValidezDocumentoTimbradoFunction(
            SetApiService setApi,
            ILogger<SetValidezDocumentoTimbradoFunction> logger)
        {
            _setApi = setApi;
            _logger = logger;
        }

        // ── GET /api/set/validez-documento-timbrado ──────────────────

        [Function("Set_ValidezDocumentoTimbrado")]
        public async Task<HttpResponseData> ValidezDocumentoTimbrado(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "set/validez-documento-timbrado")]
            HttpRequestData req)
        {
            var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            var ruc              = qs["ruc"];
            var dv               = qs["dv"];
            var numeroTimbrado   = qs["numero_timbrado"];
            var tipoDocumento    = qs["tipo_documento"];
            var numeroDocumento  = qs["numero_documento"];
            var fechaExpedicion  = qs["fecha_expedicion"];
            var medioGeneracion  = qs["medio_generacion"];

            _logger.LogInformation(
                "[Set_ValidezDocumentoTimbrado] ruc={Ruc} dv={Dv} timbrado={Timbrado} " +
                "tipo={Tipo} numero={Numero} fecha={Fecha} medio={Medio}",
                ruc, dv, numeroTimbrado, tipoDocumento,
                numeroDocumento, fechaExpedicion, medioGeneracion);

            // ── Validación de parámetros requeridos ──────────────────
            var missingParams = new List<string>();
            if (string.IsNullOrWhiteSpace(ruc))             missingParams.Add("ruc");
            if (string.IsNullOrWhiteSpace(dv))              missingParams.Add("dv");
            if (string.IsNullOrWhiteSpace(numeroTimbrado))  missingParams.Add("numero_timbrado");
            if (string.IsNullOrWhiteSpace(tipoDocumento))   missingParams.Add("tipo_documento");
            if (string.IsNullOrWhiteSpace(numeroDocumento)) missingParams.Add("numero_documento");
            if (string.IsNullOrWhiteSpace(fechaExpedicion)) missingParams.Add("fecha_expedicion");
            if (string.IsNullOrWhiteSpace(medioGeneracion)) missingParams.Add("medio_generacion");

            if (missingParams.Count > 0)
                return await BadRequest(req,
                    $"Parametros requeridos faltantes: {string.Join(", ", missingParams)}.");

            // ── Llamada a la SET ──────────────────────────────────────
            var (json, status) = await _setApi.ValidezDocumentoTimbradoAsync(
                ruc!,
                dv!,
                numeroTimbrado!,
                tipoDocumento!,
                numeroDocumento!,
                fechaExpedicion!,
                medioGeneracion!);

            return await BuildResponse(req, json, status);
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
