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

        /// <summary>Nav property de una ubicacion de documentos hacia su padre.</summary>
        private const string ParentNavigation = "parentsiteorlocation_sharepointdocumentlocation";

        /// <summary>Biblioteca y carpeta donde Dataverse espera los documentos de un registro.</summary>
        private sealed record DocumentFolder(string Library, string Folder);

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

            // El destino lo dicta Dataverse, no lo elegimos nosotros: si el PDF no cae en la
            // biblioteca y la carpeta que apunta el sharepointdocumentlocation del registro,
            // queda en SharePoint pero no aparece en la pestana Archivos de la Cita.
            var destino = await ResolveDocumentFolderAsync(serviceAppointmentId, numeroCita, cancellationToken);

            // La biblioteca es un DRIVE propio del sitio, no una carpeta dentro de la
            // biblioteca por defecto. Ver GetDriveIdAsync.
            var driveId = await _graph.GetDriveIdAsync(destino.Library, cancellationToken);

            var fileName = $"Ticket_Atencion_{Sanitize(numeroCita)}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";

            await _graph.EnsureFolderAsync(destino.Folder, driveId, cancellationToken);
            var item = await _graph.UploadAsync(
                $"{destino.Folder}/{fileName}", pdfBytes, PdfMimeType, driveId, cancellationToken);

            // La URL va al log: es el unico dato que dice donde quedo el archivo, y sin el
            // un "se subio bien pero no aparece" no se puede diagnosticar sin entrar al sitio.
            _logger.LogInformation(
                "[TicketAtencion] PDF adjuntado en {Library}/{Folder}: {Url}",
                destino.Library, destino.Folder, item.WebUrl);

            return item.WebUrl;
        }

        /// <summary>
        /// Devuelve la biblioteca y la carpeta donde Dataverse espera los documentos de esta
        /// Cita, creando el <c>sharepointdocumentlocation</c> del registro si no existe.
        ///
        /// La jerarquia de Dataverse es <b>sitio -&gt; biblioteca -&gt; carpeta</b>, y se lee
        /// en la cadena de <c>parentsiteorlocation</c>: una ubicacion cuyo padre es el SITIO
        /// nombra una <b>biblioteca de documentos</b> (una por tabla: <c>msauto_serviceappointment</c>,
        /// <c>contact</c>, ...); una cuyo padre es otra ubicacion nombra una <b>carpeta</b>
        /// dentro de esa biblioteca, con el formato <c>{nombre}_{GUID}</c> —GUID sin guiones y
        /// en mayusculas—.
        ///
        /// Tratar la biblioteca como si fuera una carpeta de la biblioteca por defecto
        /// —<c>Documentos compartidos/msauto_serviceappointment/...</c>, que es lo que hacia
        /// esta implementacion— sube el archivo sin error a una carpeta que existe, pero que
        /// no es la del registro: la pestana Archivos de la Cita sigue vacia.
        /// </summary>
        private async Task<DocumentFolder> ResolveDocumentFolderAsync(
            Guid serviceAppointmentId, string numeroCita, CancellationToken cancellationToken)
        {
            var existing = await _dataverse.GetArrayAsync(
                "sharepointdocumentlocations" +
                $"?$filter=_regardingobjectid_value eq {serviceAppointmentId} and statecode eq 0" +
                "&$select=sharepointdocumentlocationid,relativeurl" +
                $"&$expand={ParentNavigation}($select=relativeurl)&$top=1",
                "DocumentLocation", cancellationToken);

            if (existing.Count > 0)
            {
                var relativeUrl = GetString(existing[0], "relativeurl");

                // El padre de la ubicacion del registro es la biblioteca. Si por lo que sea no
                // vino, se cae al nombre de la tabla, que es como la nombra Dataverse al crearla.
                var library = GetString(existing[0], ParentNavigation, "relativeurl");

                if (string.IsNullOrWhiteSpace(library))
                    library = EntityLogicalName;

                _logger.LogInformation(
                    "[TicketAtencion] La Cita ya tiene ubicacion de documentos: {Library}/{RelativeUrl}",
                    library, relativeUrl);

                return new DocumentFolder(library, relativeUrl);
            }

            var (parentId, parentLibrary) = await GetEntityFolderLocationAsync(cancellationToken);

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
                "[TicketAtencion] Creada la ubicacion de documentos {Library}/{Folder} para la Cita.",
                parentLibrary, folderName);

            return new DocumentFolder(parentLibrary, folderName);
        }

        /// <summary>
        /// Ubicacion de documentos de la ENTIDAD (la biblioteca <c>msauto_serviceappointment</c>
        /// del sitio), que es el padre de la de cada registro. Devuelve su id y su nombre, que
        /// es el de la biblioteca.
        /// </summary>
        private async Task<(Guid Id, string Library)> GetEntityFolderLocationAsync(
            CancellationToken cancellationToken)
        {
            var rows = await _dataverse.GetArrayAsync(
                "sharepointdocumentlocations" +
                $"?$filter=relativeurl eq '{EntityLogicalName}' and _regardingobjectid_value eq null" +
                "&$select=sharepointdocumentlocationid,relativeurl&$top=1",
                "DocumentLocationRaiz", cancellationToken);

            if (rows.Count == 0)
                throw new InvalidOperationException(
                    $"No existe la ubicacion de documentos de '{EntityLogicalName}' en Dataverse. " +
                    "Hay que habilitar la integracion con SharePoint para la entidad (o abrir una " +
                    "vez la pestana Archivos de una Cita) para que Dataverse la cree.");

            return (Guid.Parse(GetString(rows[0], "sharepointdocumentlocationid")),
                    GetString(rows[0], "relativeurl"));
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

        /// <summary>Lee una propiedad de un objeto expandido. Vacio si el expand no vino.</summary>
        private static string GetString(JsonElement element, string property, string nested) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
                ? GetString(value, nested)
                : string.Empty;

        private static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
                : string.Empty;
    }
}
