using System.Net;
using System.Text.Json;
using AxxonCustomerData.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerData.Functions.Functions
{
    /// <summary>
    /// Consulta de clientes en Dataverse por RUC, para satelites externos.
    ///
    /// Endpoint expuesto:
    ///   GET /api/clientes?ruc={ruc}
    ///
    /// <b>Sin CORS, a diferencia de fiscal y de TicketAtencion.</b> El consumidor es un
    /// sistema que llama server-to-server con la function key, no un browser: un preflight
    /// anonimo seria superficie publica sin ningun caller que la use. Si algun dia lo
    /// consume un web resource, se agrega el OPTIONS y el origen se cablea desde el Bicep
    /// (<c>allowedOrigins</c>), como en TicketAtencion — nunca un <c>*</c> a mano.
    ///
    /// Es de solo lectura: no crea, no actualiza y no publica mensajes.
    /// </summary>
    public class ConsultaClientesFunction
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Los nombres ya vienen fijados por [JsonPropertyName] en los DTOs.
            WriteIndented = false
        };

        private readonly ClienteLookupService _clienteLookup;
        private readonly ILogger<ConsultaClientesFunction> _logger;

        public ConsultaClientesFunction(
            ClienteLookupService clienteLookup,
            ILogger<ConsultaClientesFunction> logger)
        {
            _clienteLookup = clienteLookup;
            _logger        = logger;
        }

        [Function("Clientes_ConsultaPorRuc")]
        public async Task<HttpResponseData> ConsultaPorRuc(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "clientes")]
            HttpRequestData req,
            CancellationToken cancellationToken)
        {
            var ruc = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["ruc"];

            _logger.LogInformation("[Clientes_ConsultaPorRuc] ruc={Ruc}", ruc);

            if (string.IsNullOrWhiteSpace(ruc))
                return await Json(req, HttpStatusCode.BadRequest,
                    "{\"error\":\"El parametro 'ruc' es requerido.\"}");

            try
            {
                var result = await _clienteLookup.FindByRucAsync(ruc, cancellationToken);
                return await Json(req, HttpStatusCode.OK, JsonSerializer.Serialize(result, JsonOpts));
            }
            catch (Exception ex)
            {
                // Dataverse caido, la MI sin Application User, o un RUC que rompe la query.
                // Se loguea con el detalle y al caller le vuelve un 502 sin internals: del
                // otro lado hay un sistema externo, no el equipo que puede leer el log.
                _logger.LogError(ex,
                    "[Clientes_ConsultaPorRuc] Error consultando Dataverse. ruc={Ruc}", ruc);

                return await Json(req, HttpStatusCode.BadGateway,
                    "{\"error\":\"Error al consultar Dataverse.\"}");
            }
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
