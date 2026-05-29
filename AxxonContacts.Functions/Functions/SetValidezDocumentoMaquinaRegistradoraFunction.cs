using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Proxy HTTP hacia el servicio oficial de Validez de Documento de Máquina Registradora de la SET Paraguay.
    /// Especificación técnica: §2.4 — Servicio Web Validez Documento Máquina Registradora (DNIT, enero/2024).
    ///
    /// Endpoint expuesto:
    ///   GET  /api/set/validez-documento-maquina-registradora
    ///       ?ruc=valorRUC
    ///       &amp;dv=valorDV
    ///       &amp;numero_timbrado=valorNumeroTimbrado
    ///       &amp;fecha_expedicion=DD/MM/AAAA
    ///       &amp;medio_generacion=2
    ///
    ///   OPTIONS /api/set/{*any}   (CORS preflight — manejado por SetConsultaRucFunction)
    ///
    /// Notas del spec:
    ///   - No requiere tipo_documento ni numero_documento (a diferencia de §2.3).
    ///   - medio_generacion = "2" (MÁQUINAS REGISTRADORAS) es el único valor válido para este servicio.
    ///
    /// Respuesta SET:
    ///   Válido   → { "mensaje": "VALIDO",   "estado": "VALIDO"   }
    ///   Inválido → { "mensaje": "..motivo..", "estado": "INVALIDO" }
    /// </summary>
    public class SetValidezDocumentoMaquinaRegistradoraFunction
    {
        private readonly SetApiService                                             _setApi;
        private readonly ILogger<SetValidezDocumentoMaquinaRegistradoraFunction>  _logger;

        public SetValidezDocumentoMaquinaRegistradoraFunction(
            SetApiService setApi,
            ILogger<SetValidezDocumentoMaquinaRegistradoraFunction> logger)
        {
            _setApi = setApi;
            _logger = logger;
        }

        // ── GET /api/set/validez-documento-maquina-registradora ──────

        [Function("Set_ValidezDocumentoMaquinaRegistradora")]
        public async Task<HttpResponseData> ValidezDocumentoMaquinaRegistradora(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                Route = "set/validez-documento-maquina-registradora")]
            HttpRequestData req)
        {
            var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            var ruc             = qs["ruc"];
            var dv              = qs["dv"];
            var numeroTimbrado  = qs["numero_timbrado"];
            var fechaExpedicion = qs["fecha_expedicion"];
            // Según spec §2.4 el único medio válido es "2" (MÁQUINAS REGISTRADORAS).
            // Se acepta como parámetro para no romper si la SET agrega más medios en el futuro,
            // pero por defecto se asigna "2" si no se especifica.
            var medioGeneracion = qs["medio_generacion"] ?? "2";

            _logger.LogInformation(
                "[Set_ValidezDocumentoMaquinaRegistradora] ruc={Ruc} dv={Dv} " +
                "timbrado={Timbrado} fecha={Fecha} medio={Medio}",
                ruc, dv, numeroTimbrado, fechaExpedicion, medioGeneracion);

            // ── Validación de parámetros requeridos ──────────────────
            var missingParams = new List<string>();
            if (string.IsNullOrWhiteSpace(ruc))             missingParams.Add("ruc");
            if (string.IsNullOrWhiteSpace(dv))              missingParams.Add("dv");
            if (string.IsNullOrWhiteSpace(numeroTimbrado))  missingParams.Add("numero_timbrado");
            if (string.IsNullOrWhiteSpace(fechaExpedicion)) missingParams.Add("fecha_expedicion");

            if (missingParams.Count > 0)
                return await BadRequest(req,
                    $"Parametros requeridos faltantes: {string.Join(", ", missingParams)}.");

            // ── Llamada a la SET ──────────────────────────────────────
            var (json, status) = await _setApi.ValidezDocumentoMaquinaRegistradoraAsync(
                ruc!,
                dv!,
                numeroTimbrado!,
                fechaExpedicion!,
                medioGeneracion);

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
