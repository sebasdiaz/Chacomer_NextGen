using System.Xml.Linq;
using AxxonTicketAtencion.Functions.Models;
using AxxonTicketAtencion.Functions.Services;

namespace AxxonTicketAtencion.Functions.Tests
{
    /// <summary>
    /// El XML es contrato con el template: los content controls estan bindeados por XPath
    /// contra esta estructura exacta. Estos tests son la red que evita romperla sin querer.
    /// </summary>
    public class TicketXmlBuilderTests
    {
        private static readonly XNamespace Ns = TicketXmlBuilder.Namespace;

        /// <summary>Todos los elementos simples que el template espera encontrar.</summary>
        public static TheoryData<string> ElementosSimples() => new()
        {
            "NombreEmpresa", "NumeroCita", "FechaRecepcion", "NombreTaller",
            "CodigoCliente", "NombreCliente", "RazonSocial", "Direccion", "Localidad", "Telefono",
            "Marca", "Modelo", "Color", "NumeroMotor", "NumeroChasis", "Patente", "CodigoProducto",
            "KmRecorrido", "Descripcion", "AsesorServicio", "TextoLegal"
        };

        [Theory]
        [MemberData(nameof(ElementosSimples))]
        public void Emite_todos_los_elementos_aunque_no_haya_datos(string elemento)
        {
            var root = Parse(TicketXmlBuilder.Build(Given.EmptyTicket()));

            // Ni ausente ni vacio: Word muestra el placeholder del content control en los
            // dos casos. Un espacio con xml:space="preserve" es lo que lo saca en blanco.
            var found = root.Element(Ns + elemento);

            Assert.NotNull(found);
            Assert.Equal(" ", found!.Value);
            Assert.Equal("preserve", found.Attribute(XNamespace.Xml + "space")?.Value);
        }

        [Fact]
        public void Usa_el_namespace_del_template()
        {
            var root = Parse(TicketXmlBuilder.Build(Given.Ticket()));

            Assert.Equal("TicketAtencion", root.Name.LocalName);
            Assert.Equal(TicketXmlBuilder.Namespace, root.Name.NamespaceName);
        }

        [Fact]
        public void Declara_encoding_utf8()
        {
            // Los bytes del part se escriben en UTF-8: una declaracion utf-16 hace que Word
            // lea mal cualquier acento.
            var xml = TicketXmlBuilder.Build(Given.Ticket());

            Assert.Contains("encoding=\"utf-8\"", xml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Mapea_los_campos_al_elemento_que_corresponde()
        {
            var root = Parse(TicketXmlBuilder.Build(Given.Ticket()));

            Assert.Equal("CA-2026-00123", Value(root, "NumeroCita"));
            Assert.Equal("Maria Gonzalez", Value(root, "NombreCliente"));
            Assert.Equal("Avda. Mariscal Lopez 1234", Value(root, "Direccion"));
            Assert.Equal("Asuncion", Value(root, "Localidad"));
            Assert.Equal("8AJFR22G1N4512345", Value(root, "NumeroChasis"));
            Assert.Equal("48250", Value(root, "KmRecorrido"));
        }

        [Fact]
        public void Repite_un_Trabajo_por_linea()
        {
            var root = Parse(TicketXmlBuilder.Build(Given.Ticket()));

            var trabajos = root.Element(Ns + "Trabajos")!.Elements(Ns + "Trabajo").ToList();

            Assert.Equal(2, trabajos.Count);
            Assert.Equal("TRB-001", trabajos[0].Element(Ns + "Codigo")!.Value);
            Assert.Equal("Alineacion y balanceo", trabajos[1].Element(Ns + "DescripcionTrabajo")!.Value);
        }

        [Fact]
        public void Repite_una_Nota_por_nota_externa()
        {
            var root = Parse(TicketXmlBuilder.Build(Given.Ticket()));

            var notas = root.Element(Ns + "NotasExternas")!.Elements(Ns + "Nota").ToList();

            Assert.Equal(2, notas.Count);
            Assert.StartsWith("El cliente retira", notas[0].Element(Ns + "Texto")!.Value);
        }

        [Fact]
        public void Mantiene_los_contenedores_aunque_no_haya_filas()
        {
            var root = Parse(TicketXmlBuilder.Build(Given.EmptyTicket()));

            Assert.NotNull(root.Element(Ns + "Trabajos"));
            Assert.NotNull(root.Element(Ns + "NotasExternas"));
            Assert.Empty(root.Element(Ns + "Trabajos")!.Elements());
        }

        [Fact]
        public void Escapa_los_caracteres_que_romperian_el_xml()
        {
            var data = Given.Ticket() with
            {
                Descripcion = "Cambio de <filtro> & revision de \"frenos\"",
                NombreCliente = "O'Brien & Asociados"
            };

            var xml = TicketXmlBuilder.Build(data);

            // Que reparsee es la prueba real de que el escapado esta bien: si faltara,
            // XDocument.Parse tiraria.
            var root = Parse(xml);

            Assert.Equal("Cambio de <filtro> & revision de \"frenos\"", Value(root, "Descripcion"));
            Assert.Equal("O'Brien & Asociados", Value(root, "NombreCliente"));
        }

        [Fact]
        public void Rechaza_un_data_nulo()
        {
            Assert.Throws<ArgumentNullException>(() => TicketXmlBuilder.Build(null!));
        }

        private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

        private static string Value(XElement root, string name) => root.Element(Ns + name)!.Value;
    }
}
