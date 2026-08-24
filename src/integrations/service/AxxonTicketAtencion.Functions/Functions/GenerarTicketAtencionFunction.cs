using System.Net;
using System.Text;
using System.Text.Json;
using AxxonTicketAtencion.Functions.Documents;
using AxxonTicketAtencion.Functions.Models;
using AxxonTicketAtencion.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AxxonTicketAtencion.Functions.Functions
{
    /// <summary>
    /// GAP-103 / GAP-227 — Ticket de Atencion (Orden de Reparacion) de una Cita de Servicio.
    ///
    /// El asesor aprieta "Generar Ticket Atencion" en el formulario de Cita de Servicio; el
    /// web resource postea el id de la cita aca y abre el .docx que devuelve la respuesta.
    /// En paralelo el documento se convierte a PDF y se adjunta a la Cita en SharePoint.
    ///
    /// Endpoint:
    ///   POST /api/GenerarTicketAtencion
    ///   Header: x-functions-key
    ///   Body:   { "serviceAppointmentId": "guid" }
    ///
    /// Codigos: 200 OK | 400 input invalido | 401 sin key | 404 cita inexistente | 500 interno.
    ///
    /// El SharePoint es best-effort: si falla, la respuesta sigue siendo 200 con
    /// status = OK_SIN_PDF y el Word utilizable. El usuario obtiene su documento igual.
    ///
    /// CORS lo resuelve la plataforma (allowed origins de la Function App, ver infra), no
    /// esta clase: emitir los headers a mano ademas de la configuracion del host duplica
    /// Access-Control-Allow-Origin y el browser rechaza la respuesta.
    /// </summary>
    public class GenerarTicketAtencionFunction
    {
        private readonly ITicketAtencionDataService _data;
        private readonly ITicketDocumentBuilder _documents;
        private readonly ITicketSharePointService _sharePoint;
        private readonly ILogger<GenerarTicketAtencionFunction> _logger;

        public GenerarTicketAtencionFunction(
            ITicketAtencionDataService data,
            ITicketDocumentBuilder documents,
            ITicketSharePointService sharePoint,
            ILogger<GenerarTicketAtencionFunction> logger)
        {
            _data       = data;
            _documents  = documents;
            _sharePoint = sharePoint;
            _logger     = logger;
        }

        [Function("GenerarTicketAtencion")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            // try/catch global: cualquier excepcion sale como respuesta tipada. Sin esto un
            // body sin la propiedad esperada termina en un 500 opaco.
            try
            {
                if (!TryReadServiceAppointmentId(await ReadBodyAsync(req), out var saId, out var error))
                    return await ErrorAsync(req, HttpStatusCode.BadRequest, error);

                _logger.LogInformation("[TicketAtencion] Cita {ServiceAppointmentId}.", saId);

                var data = await _data.GetAsync(saId, cancellationToken);

                if (data is null)
                    return await ErrorAsync(req, HttpStatusCode.NotFound,
                        "No se encontro la Cita de Servicio indicada.");

                var xml       = TicketXmlBuilder.Build(data);
                var wordBytes = _documents.Build(xml);

                var response = new GenerarTicketAtencionResponse
                {
                    Status     = TicketStatus.Ok,
                    WordBase64 = Convert.ToBase64String(wordBytes),
                    FileName   = $"Ticket_Atencion_{FileNameFor(data.NumeroCita, saId)}.docx",
                    WordBytes  = wordBytes.Length
                };

                // A partir de aca el Word ya esta: nada de lo que siga puede hacer fallar
                // la respuesta.
                try
                {
                    response.Url = await _sharePoint.AttachPdfAsync(
                        saId, data.NumeroCita, wordBytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    // El detalle va a Application Insights, no al cliente.
                    _logger.LogError(ex,
                        "[TicketAtencion] Fallo el PDF/SharePoint de la cita {ServiceAppointmentId}. " +
                        "Se devuelve el Word igual.", saId);

                    response.Status = TicketStatus.OkSinPdf;
                    response.Url    = string.Empty;
                }

                return await JsonAsync(req, HttpStatusCode.OK, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TicketAtencion] Error generando el ticket.");
                return await ErrorAsync(req, HttpStatusCode.InternalServerError,
                    "No se pudo generar el Ticket de Atencion. Reintentar; si persiste, " +
                    "contactar al equipo de sistemas.");
            }
        }

        // -- Input ---------------------------------------------------------

        private static async Task<string> ReadBodyAsync(HttpRequestData req)
        {
            using var reader = new StreamReader(req.Body);
            return await reader.ReadToEndAsync();
        }

        /// <summary>
        /// Valida el body y devuelve el GUID de la cita.
        ///
        /// El Guid.TryParse no es cosmetico: el id termina interpolado en filtros OData, y
        /// aceptarlo como string crudo es una inyeccion OData sobre un endpoint expuesto.
        /// Tipar el resultado como Guid hace que la validacion no se pueda saltear aguas abajo.
        /// </summary>
        private static bool TryReadServiceAppointmentId(string body, out Guid serviceAppointmentId, out string error)
        {
            serviceAppointmentId = Guid.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(body))
            {
                error = "El body de la solicitud esta vacio.";
                return false;
            }

            GenerarTicketAtencionRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<GenerarTicketAtencionRequest>(body);
            }
            catch (JsonException)
            {
                error = "El body de la solicitud no es un JSON valido.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request?.ServiceAppointmentId))
            {
                error = "Falta 'serviceAppointmentId' en el body de la solicitud.";
                return false;
            }

            if (!Guid.TryParse(request.ServiceAppointmentId, out serviceAppointmentId) ||
                serviceAppointmentId == Guid.Empty)
            {
                error = "'serviceAppointmentId' no es un identificador valido.";
                return false;
            }

            return true;
        }

        // -- Output --------------------------------------------------------

        /// <summary>Numero de cita para el nombre del archivo, con el id como respaldo.</summary>
        private static string FileNameFor(string numeroCita, Guid saId) =>
            string.IsNullOrWhiteSpace(numeroCita) ? saId.ToString() : numeroCita;

        // Serializacion a mano en lugar de WriteAsJsonAsync: la sobrecarga corta de la
        // extension fuerza el status a 200, y aca hacen falta 400/404/500 con cuerpo JSON.
        private static async Task<HttpResponseData> JsonAsync<T>(
            HttpRequestData req, HttpStatusCode status, T payload)
        {
            var response = req.CreateResponse(status);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(payload), Encoding.UTF8);
            return response;
        }

        private static Task<HttpResponseData> ErrorAsync(
            HttpRequestData req, HttpStatusCode status, string mensaje) =>
            JsonAsync(req, status, new TicketErrorResponse { Mensaje = mensaje });
    }
}
