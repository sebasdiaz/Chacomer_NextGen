using System.Text.Json;
using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Graph;
using Microsoft.Extensions.Logging;

namespace AxxonTicketAtencion.Functions.Services
{
    /// <summary>Convierte el ticket a PDF y lo adjunta a la Cita de Servicio.</summary>
    public interface ITicketSharePointService
    {
        /// <summary>
        /// Devuelve la URL del PDF adjuntado. Toda esta rama es best-effort: el caller la
        /// envuelve en try/catch y responde igual con el Word.
        /// </summary>
        Task<string> AttachPdfAsync(
            Guid serviceAppointmentId, string numeroCita, byte[] wordBytes, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="ITicketSharePointService"/>
    public sealed class TicketSharePointService : ITicketSharePointService
    {
        /// <summary>Nombre logico de la entidad. Es tambien el de su carpeta raiz en SharePoint.</summary>
        public const string EntityLogicalName = "msauto_serviceappointment";

        public const string EntitySetName = "msauto_serviceappointments";

        private const string WordMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        private const string PdfMimeType  = "application/pdf";
        private const string TempFolder   = "Temp/TicketAtencion";

        private readonly IGraphSharePointService _graph;
        private readonly IDataverseWebApiClient _dataverse;
        private readonly ILogger<TicketSharePointService> _logger;

        public TicketSharePointService(
            IGraphSharePointService graph,
            IDataverseWebApiClient dataverse,
            ILogger<TicketSharePointService> logger)
        {
            _graph     = graph;
            _dataverse = dataverse;
            _logger    = logger;
        }

        public async Task<string> AttachPdfAsync(
            Guid serviceAppointmentId, string numeroCita, byte[] wordBytes, CancellationToken cancellationToken = default)
        {
            var pdfBytes = await _graph.ConvertToPdfAsync(
                wordBytes, TempFolder, ".docx", WordMimeType, cancellationToken);

            _logger.LogInformation("[TicketAtencion] PDF generado ({Bytes} bytes).", pdfBytes.Length);

            // La carpeta la dicta Dataverse, no la elegimos nosotros: si el PDF no cae en la
            // que apunta el sharepointdocumentlocation del registro, queda en SharePoint pero
            // no aparece en la pestana Archivos de la Cita.
            var folder = await ResolveDocumentFolderAsync(serviceAppointmentId, numeroCita, cancellationToken);

            var fileName = $"Ticket_Atencion_{Sanitize(numeroCita)}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";

            await _graph.EnsureFolderAsync(folder, cancellationToken);
            var item = await _graph.UploadAsync($"{folder}/{fileName}", pdfBytes, PdfMimeType, cancellationToken);

            _logger.LogInformation("[TicketAtencion] PDF adjuntado en {Folder}.", folder);
            return item.WebUrl;
        }

        /// <summary>
        /// Devuelve la carpeta del drive donde Dataverse espera los documentos de esta Cita,
        /// creando el <c>sharepointdocumentlocation</c> si todavia no existe.
        ///
        /// La convencion de Dataverse es <c>{carpeta-de-la-entidad}/{nombre}_{GUID}</c>, con
        /// el GUID sin guiones y en mayusculas. Subir a <c>{entidad}/{saId}</c> —como hacia la
        /// implementacion original— deja el archivo en el sitio pero desasociado del registro.
        /// </summary>
        private async Task<string> ResolveDocumentFolderAsync(
            Guid serviceAppointmentId, string numeroCita, CancellationToken cancellationToken)
        {
            var existing = await _dataverse.GetArrayAsync(
                "sharepointdocumentlocations" +
                $"?$filter=_regardingobjectid_value eq {serviceAppointmentId} and statecode eq 0" +
                "&$select=sharepointdocumentlocationid,relativeurl&$top=1",
                "DocumentLocation", cancellationToken);

            if (existing.Count > 0)
            {
                var relativeUrl = GetString(existing[0], "relativeurl");
                _logger.LogInformation(
                    "[TicketAtencion] La Cita ya tiene ubicacion de documentos: {RelativeUrl}", relativeUrl);
                return $"{EntityLogicalName}/{relativeUrl}";
            }

            var parentId = await GetEntityFolderLocationIdAsync(cancellationToken);

            // {nombre}_{GUID sin guiones, mayusculas}: es lo que arma Dataverse cuando crea
            // la carpeta desde la UI, y lo que espera al resolver el link.
            var folderName = $"{Sanitize(numeroCita)}_{serviceAppointmentId.ToString("N").ToUpperInvariant()}";

            await _dataverse.CreateAsync("sharepointdocumentlocations", new Dictionary<string, object?>
            {
                ["name"]        = $"Documentos de {numeroCita}",
                ["relativeurl"] = folderName,
                ["parentsiteorlocation_sharepointdocumentlocation@odata.bind"] =
                    $"/sharepointdocumentlocations({parentId})",
                [$"regardingobjectid_{EntityLogicalName}@odata.bind"] =
                    $"/{EntitySetName}({serviceAppointmentId})"
            }, "DocumentLocation", cancellationToken);

            _logger.LogInformation(
                "[TicketAtencion] Creada la ubicacion de documentos {Folder} para la Cita.", folderName);

            return $"{EntityLogicalName}/{folderName}";
        }

        /// <summary>
        /// Ubicacion de documentos de la ENTIDAD (la carpeta <c>msauto_serviceappointment</c>
        /// del sitio), que es el padre de la de cada registro.
        /// </summary>
        private async Task<Guid> GetEntityFolderLocationIdAsync(CancellationToken cancellationToken)
        {
            var rows = await _dataverse.GetArrayAsync(
                "sharepointdocumentlocations" +
                $"?$filter=relativeurl eq '{EntityLogicalName}' and _regardingobjectid_value eq null" +
                "&$select=sharepointdocumentlocationid&$top=1",
                "DocumentLocationRaiz", cancellationToken);

            if (rows.Count == 0)
                throw new InvalidOperationException(
                    $"No existe la ubicacion de documentos de '{EntityLogicalName}' en Dataverse. " +
                    "Hay que habilitar la integracion con SharePoint para la entidad (o abrir una " +
                    "vez la pestana Archivos de una Cita) para que Dataverse la cree.");

            return Guid.Parse(GetString(rows[0], "sharepointdocumentlocationid"));
        }

        /// <summary>Saca de un identificador lo que SharePoint no acepta en nombres.</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "SinNumero";

            var invalid = new[] { '"', '*', ':', '<', '>', '?', '/', '\\', '|', '#', '%' };
            var clean   = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();

            return string.IsNullOrEmpty(clean) ? "SinNumero" : clean;
        }

        private static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
                : string.Empty;
    }
}
