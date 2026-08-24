using System.Xml.Linq;
using AxxonTicketAtencion.Functions.Documents;
using AxxonTicketAtencion.Functions.Services;
using DocumentFormat.OpenXml.CustomXmlDataProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace AxxonTicketAtencion.Functions.Tests
{
    /// <summary>
    /// Tests del relleno del .docx contra el MISMO template que se despliega.
    ///
    /// Lo que se protege aca es lo que la implementacion original rompia: borraba el
    /// Custom XML Part y agregaba uno nuevo, perdiendo el storeItemID al que apuntan los
    /// w:dataBinding de los content controls.
    /// </summary>
    public class TicketDocumentBuilderTests
    {
        private static readonly XNamespace Ns = TicketXmlBuilder.Namespace;

        private static string TemplatePath =>
            Path.Combine(AppContext.BaseDirectory, "Templates", TicketDocumentBuilder.TemplateFileName);

        private static TicketDocumentBuilder Builder() =>
            new(NullLogger<TicketDocumentBuilder>.Instance, TemplatePath);

        [Fact]
        public void El_template_viaja_con_el_proyecto()
        {
            Assert.True(File.Exists(TemplatePath),
                $"No se encontro el template en {TemplatePath}.");
        }

        [Fact]
        public void Genera_un_docx_que_abre()
        {
            var bytes = Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));

            Assert.NotEmpty(bytes);

            using var stream = new MemoryStream(bytes);
            using var doc    = WordprocessingDocument.Open(stream, isEditable: false);

            Assert.NotNull(doc.MainDocumentPart);
        }

        [Fact]
        public void Inyecta_los_datos_en_el_custom_xml_part()
        {
            var bytes = Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));

            var root = ReadMappedPart(bytes);

            Assert.Equal("CA-2026-00123", root.Element(Ns + "NumeroCita")!.Value);
            Assert.Equal(2, root.Element(Ns + "Trabajos")!.Elements(Ns + "Trabajo").Count());
        }

        [Fact]
        public void Preserva_el_storeItemID_al_que_apuntan_los_content_controls()
        {
            var original = StoreItemIdOf(File.ReadAllBytes(TemplatePath));

            var bytes  = Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));
            var actual = StoreItemIdOf(bytes);

            // Si esto falla, los w:dataBinding del documento apuntan a un datastore que ya
            // no existe: Word suele salvarlo por namespace, pero Graph al convertir a PDF no.
            Assert.Equal(original, actual);
        }

        [Fact]
        public void Los_dataBinding_siguen_apuntando_a_un_part_existente()
        {
            var bytes = Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));

            using var stream = new MemoryStream(bytes);
            using var doc    = WordprocessingDocument.Open(stream, isEditable: false);

            var main = doc.MainDocumentPart!;

            var storeIds = main.Document.Descendants<DataBinding>()
                .Select(b => b.StoreItemId?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            Assert.NotEmpty(storeIds);

            var existentes = main.CustomXmlParts
                .Select(p => p.CustomXmlPropertiesPart?.DataStoreItem?.ItemId?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.All(storeIds, id => Assert.Contains(id!, existentes));
        }

        [Fact]
        public void No_modifica_el_archivo_del_paquete()
        {
            var before = File.ReadAllBytes(TemplatePath);

            Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));

            Assert.Equal(before, File.ReadAllBytes(TemplatePath));
        }

        [Fact]
        public void Genera_igual_sin_datos()
        {
            // Una cita recien creada casi no tiene datos: el documento tiene que salir igual.
            var bytes = Builder().Build(TicketXmlBuilder.Build(Given.EmptyTicket()));

            var root = ReadMappedPart(bytes);

            Assert.Equal(string.Empty, root.Element(Ns + "NumeroCita")!.Value);
            Assert.Empty(root.Element(Ns + "Trabajos")!.Elements());
        }

        [Fact]
        public void Falla_con_un_mensaje_util_si_falta_el_template()
        {
            var builder = new TicketDocumentBuilder(
                NullLogger<TicketDocumentBuilder>.Instance,
                Path.Combine(AppContext.BaseDirectory, "Templates", "no-existe.docx"));

            var ex = Assert.Throws<FileNotFoundException>(
                () => builder.Build(TicketXmlBuilder.Build(Given.Ticket())));

            Assert.Contains("paquete de deployment", ex.Message);
        }

        [Fact]
        public void Rechaza_un_xml_vacio()
        {
            Assert.ThrowsAny<ArgumentException>(() => Builder().Build("   "));
        }

        /// <summary>
        /// Deja un .docx poblado en la carpeta de salida del test para inspeccionarlo a mano
        /// en Word. Es el reemplazo del generador de prueba que antes se empaquetaba con la
        /// Function y escribia al Desktop del que lo corriera.
        /// </summary>
        [Fact]
        public void Deja_un_ejemplar_inspeccionable_en_la_salida_del_test()
        {
            var bytes  = Builder().Build(TicketXmlBuilder.Build(Given.Ticket()));
            var output = Path.Combine(AppContext.BaseDirectory, "ticket-atencion-ejemplo.docx");

            File.WriteAllBytes(output, bytes);

            Assert.True(new FileInfo(output).Length > 0);
        }

        // -- Helpers -------------------------------------------------------

        private static XElement ReadMappedPart(byte[] docx)
        {
            using var stream = new MemoryStream(docx);
            using var doc    = WordprocessingDocument.Open(stream, isEditable: false);

            var part = doc.MainDocumentPart!.CustomXmlParts.Single(DeclaresTicketNamespace);

            using var content = part.GetStream();
            return XDocument.Load(content).Root!;
        }

        private static string StoreItemIdOf(byte[] docx)
        {
            using var stream = new MemoryStream(docx);
            using var doc    = WordprocessingDocument.Open(stream, isEditable: false);

            var part = doc.MainDocumentPart!.CustomXmlParts.Single(DeclaresTicketNamespace);

            return part.CustomXmlPropertiesPart!.DataStoreItem!.ItemId!.Value!;
        }

        private static bool DeclaresTicketNamespace(CustomXmlPart part) =>
            part.CustomXmlPropertiesPart?.DataStoreItem?.SchemaReferences?
                .Elements<SchemaReference>()
                .Any(r => r.Uri?.Value == TicketXmlBuilder.Namespace) == true;
    }
}
