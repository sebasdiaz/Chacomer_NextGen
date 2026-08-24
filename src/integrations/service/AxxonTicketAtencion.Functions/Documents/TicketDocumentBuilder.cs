using System.Text;
using DocumentFormat.OpenXml.CustomXmlDataProperties;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;

namespace AxxonTicketAtencion.Functions.Documents
{
    /// <summary>Genera el .docx del Ticket de Atencion a partir del XML de datos.</summary>
    public interface ITicketDocumentBuilder
    {
        /// <summary>Devuelve los bytes del .docx con el Custom XML Part reemplazado.</summary>
        byte[] Build(string xmlData);
    }

    /// <inheritdoc cref="ITicketDocumentBuilder"/>
    public sealed class TicketDocumentBuilder : ITicketDocumentBuilder
    {
        public const string TemplateFileName = "template_ticket_atencion.docx";

        private readonly ILogger<TicketDocumentBuilder> _logger;
        private readonly string _templatePath;

        public TicketDocumentBuilder(ILogger<TicketDocumentBuilder> logger, string? templatePath = null)
        {
            _logger       = logger;
            _templatePath = templatePath
                ?? Path.Combine(AppContext.BaseDirectory, "Templates", TemplateFileName);
        }

        public byte[] Build(string xmlData)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(xmlData);

            if (!File.Exists(_templatePath))
                throw new FileNotFoundException(
                    $"No se encontro el template '{TemplateFileName}'. Verificar que viaje en el " +
                    "paquete de deployment (CopyToPublishDirectory en el .csproj).",
                    _templatePath);

            // Copia en memoria: el archivo del paquete no se toca nunca. Es de solo lectura
            // en Flex Consumption, y ademas lo comparten todas las invocaciones.
            var stream = new MemoryStream();
            using (var template = File.OpenRead(_templatePath))
                template.CopyTo(stream);
            stream.Position = 0;

            using (var doc = WordprocessingDocument.Open(stream, isEditable: true))
            {
                var main = doc.MainDocumentPart
                    ?? throw new InvalidOperationException("El template no tiene MainDocumentPart.");

                var part = FindMappedPart(main)
                    ?? throw new InvalidOperationException(
                        "El template no tiene Custom XML Parts. Los content controls no tienen " +
                        "contra que bindear.");

                // Se SOBRESCRIBE el part existente en lugar de borrarlo y crear uno nuevo.
                // Borrarlo descarta el CustomXmlPropertiesPart, y con el, el storeItemID al
                // que apuntan los w:dataBinding de los content controls. Word suele tolerarlo
                // cayendo al binding por namespace; la conversion a PDF de Graph no siempre.
                using var data = new MemoryStream(new UTF8Encoding(false).GetBytes(xmlData));
                part.FeedData(data);
            }

            var bytes = stream.ToArray();
            _logger.LogInformation("[TicketAtencion] Documento generado ({Bytes} bytes).", bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Devuelve el Custom XML Part cuyo datastore declara el namespace del ticket.
        /// Un .docx puede tener varios (Word agrega los suyos de metadata), asi que elegir
        /// "el primero" es una loteria: se busca por schemaRef y recien despues se cae al
        /// primero como ultimo recurso.
        /// </summary>
        private CustomXmlPart? FindMappedPart(MainDocumentPart main)
        {
            var mapped = main.CustomXmlParts.FirstOrDefault(DeclaresTicketNamespace);

            if (mapped is not null)
                return mapped;

            var fallback = main.CustomXmlParts.FirstOrDefault();

            if (fallback is not null)
                _logger.LogWarning(
                    "[TicketAtencion] Ningun Custom XML Part declara el namespace '{Namespace}'. " +
                    "Se usa el primero. Revisar el XML Mapping del template.",
                    Services.TicketXmlBuilder.Namespace);

            return fallback;
        }

        private static bool DeclaresTicketNamespace(CustomXmlPart part)
        {
            var references = part.CustomXmlPropertiesPart?.DataStoreItem?.SchemaReferences;

            return references is not null
                && references.Elements<SchemaReference>()
                    .Any(r => string.Equals(
                        r.Uri?.Value,
                        Services.TicketXmlBuilder.Namespace,
                        StringComparison.OrdinalIgnoreCase));
        }
    }
}
