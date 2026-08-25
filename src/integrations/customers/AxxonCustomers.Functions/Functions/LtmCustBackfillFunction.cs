using System.Net;
using System.Text.Json;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Functions
{
    /// <summary>
    /// Encola los clientes que ya existen en F&amp;O para que se les escriba la fila de
    /// <c>LTMCustTable</c>. Cubre lo creado antes de que existiera esta integracion.
    ///
    /// <b>Por que HTTP y no un timer.</b> Es un proceso de una sola vez, y la v1 escribe con
    /// un POST sin verificar si la fila existe: una segunda corrida manda al DLQ todo lo que
    /// la primera ya escribio. Un timer "que despues deshabilitamos" depende de que alguien
    /// se acuerde de deshabilitarlo — el dia que no pase, repite el maestro de clientes
    /// entero contra el DLQ, todos los dias. Un HTTP trigger se dispara cuando se lo llama y
    /// no hay nada que apagar despues.
    ///
    /// Endpoints:
    ///   POST /api/ltm/backfill?entity=contact&amp;dryRun=true    → cuenta, no encola
    ///   POST /api/ltm/backfill?entity=contact&amp;max=50        → encola los primeros 50
    ///   POST /api/ltm/backfill?entity=account                 → encola todos
    ///
    /// <c>dryRun</c> primero, siempre: con un maestro de clientes conviene saber el volumen
    /// antes de encolarlo.
    /// </summary>
    public class LtmCustBackfillFunction
    {
        private readonly ILtmCustBackfillService _backfill;
        private readonly ILogger<LtmCustBackfillFunction> _logger;

        public LtmCustBackfillFunction(
            ILtmCustBackfillService backfill,
            ILogger<LtmCustBackfillFunction> logger)
        {
            _backfill = backfill;
            _logger   = logger;
        }

        [Function(nameof(LtmCustBackfillFunction))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ltm/backfill")]
            HttpRequestData req,
            CancellationToken cancellationToken)
        {
            var qs     = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var entity = qs["entity"];
            var source = LtmCustSource.For(entity);

            if (source is null)
                return await Error(req, HttpStatusCode.BadRequest,
                    $"El parametro 'entity' tiene que ser 'contact' o 'account' (llego: '{entity ?? "vacio"}').");

            // Sin el parametro, dryRun. Que el default sea "no escribas nada" es
            // deliberado: el modo destructivo se pide explicitamente.
            var dryRun = !string.Equals(qs["dryRun"], "false", StringComparison.OrdinalIgnoreCase);

            int? max = null;
            if (!string.IsNullOrWhiteSpace(qs["max"]))
            {
                if (!int.TryParse(qs["max"], out var parsed) || parsed <= 0)
                    return await Error(req, HttpStatusCode.BadRequest,
                        $"El parametro 'max' tiene que ser un entero positivo (llego: '{qs["max"]}').");

                max = parsed;
            }

            _logger.LogInformation(
                "[LtmCustBackfillFunction] Backfill pedido. Entidad={Entity} | DryRun={DryRun} | Max={Max}",
                source.EntityLogicalName, dryRun, max?.ToString() ?? "sin limite");

            try
            {
                var result = await _backfill.RunAsync(source, dryRun, max, cancellationToken);
                return await Json(req, HttpStatusCode.OK, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[LtmCustBackfillFunction] El backfill de {Entity} fallo: {Error}",
                    source.EntityLogicalName, ex.Message);

                return await Error(req, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private static async Task<HttpResponseData> Json(
            HttpRequestData req, HttpStatusCode status, object body)
        {
            var response = req.CreateResponse(status);
            await response.WriteStringAsync(JsonSerializer.Serialize(body));
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            return response;
        }

        private static Task<HttpResponseData> Error(
            HttpRequestData req, HttpStatusCode status, string message) =>
            Json(req, status, new { error = message });
    }
}
