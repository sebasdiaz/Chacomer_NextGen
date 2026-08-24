using System.Xml;
using System.Xml.Linq;
using AxxonTicketAtencion.Functions.Models;

namespace AxxonTicketAtencion.Functions.Services
{
    /// <summary>
    /// Arma el XML que se inyecta como Custom XML Part del .docx.
    ///
    /// Los content controls del template estan bindeados por XPath contra este namespace
    /// (ver <c>w:dataBinding</c> en word/document.xml), asi que la forma del arbol y los
    /// nombres de los elementos son contrato con el template: no se tocan de un lado sin
    /// el otro.
    ///
    /// Dos reglas que el binding impone:
    ///   - Todo elemento se emite SIEMPRE, aunque venga vacio. Un elemento ausente deja al
    ///     content control mostrando su placeholder.
    ///   - Todo valor va escapado. Se usa XDocument en lugar de concatenar strings
    ///     justamente para que el escapado no sea opcional.
    /// </summary>
    public static class TicketXmlBuilder
    {
        /// <summary>Namespace del Custom XML Part. Coincide con el schemaRef del template.</summary>
        public const string Namespace = "http://Chacomer.TicketAtencion";

        private static readonly XNamespace Ns = Namespace;

        public static string Build(TicketAtencionData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var root = new XElement(Ns + "TicketAtencion",
                Element("NombreEmpresa",  data.NombreEmpresa),
                Element("NumeroCita",     data.NumeroCita),
                Element("FechaRecepcion", data.FechaRecepcion),
                Element("NombreTaller",   data.NombreTaller),

                Element("CodigoCliente", data.CodigoCliente),
                Element("NombreCliente", data.NombreCliente),
                Element("RazonSocial",   data.RazonSocial),
                Element("Direccion",     data.Direccion),
                Element("Localidad",     data.Localidad),
                Element("Telefono",      data.Telefono),

                Element("Marca",          data.Marca),
                Element("Modelo",         data.Modelo),
                Element("Color",          data.Color),
                Element("NumeroMotor",    data.NumeroMotor),
                Element("NumeroChasis",   data.NumeroChasis),
                Element("Patente",        data.Patente),
                Element("CodigoProducto", data.CodigoProducto),
                Element("KmRecorrido",    data.KmRecorrido),

                Element("Descripcion",    data.Descripcion),
                Element("AsesorServicio", data.AsesorServicio),
                Element("TextoLegal",     data.TextoLegal),

                new XElement(Ns + "Trabajos",
                    data.Trabajos.Select(t => new XElement(Ns + "Trabajo",
                        Element("Codigo",             t.Codigo),
                        Element("DescripcionTrabajo", t.Descripcion)))),

                new XElement(Ns + "NotasExternas",
                    data.NotasExternas.Select(n => new XElement(Ns + "Nota",
                        Element("Texto", n)))));

            var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);

            // Utf8StringWriter y no StringWriter: XmlWriter toma el encoding de la
            // declaracion del TextWriter, y un StringWriter comun declara utf-16 —
            // que despues no coincide con los bytes UTF-8 que se escriben en el part.
            using var writer = new Utf8StringWriter();
            using (var xml = XmlWriter.Create(writer, new XmlWriterSettings
                   {
                       Indent             = true,
                       OmitXmlDeclaration = false,
                       Encoding           = System.Text.Encoding.UTF8
                   }))
            {
                document.Save(xml);
            }

            return writer.ToString();
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        }

        // Un valor null se emite como elemento vacio, nunca se omite: el binding del
        // template espera el elemento presente.
        private static XElement Element(string name, string? value) =>
            new(Ns + name, value ?? string.Empty);
    }
}
