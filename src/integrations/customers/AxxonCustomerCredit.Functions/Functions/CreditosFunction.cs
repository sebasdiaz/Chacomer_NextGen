using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using AxxonCustomerCredit.Functions.Models;
using AxxonCustomerCredit.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerCredit.Functions.Functions
{
    /// <summary>
    /// Los cuatro endpoints de solo lectura sobre las entidades de credito de F&amp;O,
    /// para que los satelites traigan la ficha crediticia, los planes otorgados, sus
    /// cuotas y las resoluciones de las solicitudes.
    ///
    /// Endpoints:
    ///   GET /api/creditos/clientes      ?dataAreaId=&amp;cuenta=&amp;top=
    ///   GET /api/creditos/planes        ?dataAreaId=&amp;cuenta=&amp;creditId=&amp;requestId=&amp;top=
    ///   GET /api/creditos/cuotas        ?dataAreaId=&amp;cuenta=&amp;creditId=&amp;top=
    ///   GET /api/creditos/resoluciones  ?dataAreaId=&amp;solicitudId=&amp;top=
    ///
    /// <b>Sin CORS</b>, con el mismo criterio que
    /// [Customer Data](docs/wiki/integraciones/customerdata.md): el consumidor es un
    /// sistema que llama server-to-server con la function key, no un browser. Si algun dia
    /// lo consume un web resource se agrega el OPTIONS y el origen se cablea desde el
    /// Bicep (<c>allowedOrigins</c>) — nunca un <c>*</c> a mano.
    ///
    /// Es de solo lectura: no crea, no actualiza y no publica mensajes.
    /// </summary>
    public class CreditosFunction
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Los nombres ya vienen fijados por [JsonPropertyName] en los DTOs.
            WriteIndented = false
        };

        /// <summary>Aceptado por los cuatro endpoints.</summary>
        private const string ParamTop = "top";

        private readonly IFoCreditoService _creditos;
        private readonly ILogger<CreditosFunction> _logger;

        public CreditosFunction(IFoCreditoService creditos, ILogger<CreditosFunction> logger)
        {
            _creditos = creditos;
            _logger   = logger;
        }

        [Function("Creditos_Clientes")]
        public Task<HttpResponseData> Clientes(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "creditos/clientes")]
            HttpRequestData req,
            CancellationToken cancellationToken) =>
            ResponderAsync(req, "Creditos_Clientes",
                ["dataAreaId", "cuenta"],
                (c, ct) => _creditos.GetClientesAsync(c, ct),
                cancellationToken);

        [Function("Creditos_Planes")]
        public Task<HttpResponseData> Planes(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "creditos/planes")]
            HttpRequestData req,
            CancellationToken cancellationToken) =>
            ResponderAsync(req, "Creditos_Planes",
                ["dataAreaId", "cuenta", "creditId", "requestId"],
                (c, ct) => _creditos.GetPlanesAsync(c, ct),
                cancellationToken);

        [Function("Creditos_Cuotas")]
        public Task<HttpResponseData> Cuotas(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "creditos/cuotas")]
            HttpRequestData req,
            CancellationToken cancellationToken) =>
            ResponderAsync(req, "Creditos_Cuotas",
                ["dataAreaId", "cuenta", "creditId"],
                (c, ct) => _creditos.GetCuotasAsync(c, ct),
                cancellationToken);

        [Function("Creditos_Resoluciones")]
        public Task<HttpResponseData> Resoluciones(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "creditos/resoluciones")]
            HttpRequestData req,
            CancellationToken cancellationToken) =>
            ResponderAsync(req, "Creditos_Resoluciones",
                // Sin "cuenta": DevAxCustCreditResolutions no tiene CustomerAccount.
                ["dataAreaId", "solicitudId"],
                (c, ct) => _creditos.GetResolucionesAsync(c, ct),
                cancellationToken);

        /// <summary>
        /// El cuerpo comun de los cuatro: valida la query string, llama al servicio y
        /// arma la respuesta. Los endpoints solo declaran que filtros aceptan y a quien
        /// le preguntan.
        /// </summary>
        private async Task<HttpResponseData> ResponderAsync<T>(
            HttpRequestData req,
            string nombre,
            string[] filtrosPermitidos,
            Func<CreditoConsulta, CancellationToken, Task<CreditoResultado<T>>> leer,
            CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            _logger.LogInformation("[{Funcion}] query={Query}", nombre, req.Url.Query);

            if (!TryParseConsulta(query, filtrosPermitidos, out var consulta, out var error))
                return await Json(req, HttpStatusCode.BadRequest,
                    JsonSerializer.Serialize(new { error }, JsonOpts));

            try
            {
                var resultado = await leer(consulta, cancellationToken);

                var respuesta = new CreditoResponse<T>
                {
                    Truncado = resultado.Truncado,
                    Datos    = resultado.Items
                };

                return await Json(req, HttpStatusCode.OK,
                    JsonSerializer.Serialize(respuesta, JsonOpts));
            }
            catch (Exception ex)
            {
                // F&O caido, la identidad sin permiso sobre la entidad, o un filtro que el
                // ERP rechaza. Se loguea con el detalle y al caller le vuelve un 502 sin
                // internals: del otro lado hay un sistema externo, no el equipo que puede
                // leer App Insights.
                _logger.LogError(ex,
                    "[{Funcion}] Error consultando F&O. query={Query}", nombre, req.Url.Query);

                return await Json(req, HttpStatusCode.BadGateway,
                    "{\"error\":\"Error al consultar F&O.\"}");
            }
        }

        /// <summary>
        /// Valida la query string contra los filtros que acepta el endpoint.
        ///
        /// <b>Un parametro que el endpoint no soporta es un 400, no algo que se ignora.</b>
        /// Ignorarlo devolveria un 200 con la tabla entera y el consumidor creeria que
        /// filtro — el caso concreto es <c>cuenta</c> en resoluciones, que no existe en
        /// esa entidad.
        /// </summary>
        private static bool TryParseConsulta(
            NameValueCollection query,
            string[] filtrosPermitidos,
            out CreditoConsulta consulta,
            out string error)
        {
            consulta = new CreditoConsulta();
            error    = string.Empty;

            foreach (var clave in query.AllKeys)
            {
                if (string.IsNullOrEmpty(clave)) continue;
                if (clave.Equals(ParamTop, StringComparison.OrdinalIgnoreCase)) continue;
                if (filtrosPermitidos.Contains(clave, StringComparer.OrdinalIgnoreCase)) continue;

                error = $"El parametro '{clave}' no aplica a este endpoint. " +
                        $"Acepta: {string.Join(", ", filtrosPermitidos)}, {ParamTop}.";
                return false;
            }

            var top = CreditoLimites.TopDefault;
            var topRaw = query[ParamTop];
            if (!string.IsNullOrWhiteSpace(topRaw))
            {
                if (!int.TryParse(topRaw, out top) ||
                    top < 1 || top > CreditoLimites.TopMaximo)
                {
                    error = $"El parametro 'top' debe ser un entero entre 1 y " +
                            $"{CreditoLimites.TopMaximo}.";
                    return false;
                }
            }

            consulta = new CreditoConsulta
            {
                DataAreaId  = query["dataAreaId"],
                Cuenta      = query["cuenta"],
                CreditId    = query["creditId"],
                RequestId   = query["requestId"],
                SolicitudId = query["solicitudId"],
                Top         = top
            };

            return true;
        }

        private static async Task<HttpResponseData> Json(
            HttpRequestData req, HttpStatusCode status, string json)
        {
            var resp = req.CreateResponse(status);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await resp.WriteStringAsync(json);
            return resp;
        }
    }
}
