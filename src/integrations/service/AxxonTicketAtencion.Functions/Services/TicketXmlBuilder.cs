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
    ///   - Todo elemento se emite SIEMPRE, y NUNCA vacio. Word muestra el placeholder del
    ///     content control ("Click or tap here to enter text.") tanto cuando el nodo
    ///     bindeado falta como cuando esta vacio, asi que un dato ausente va como un
    ///     espacio. Ver <see cref="Element"/>.
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

                new XElement(Ns + "Trabajos",  Trabajos(data.Trabajos)),
                new XElement(Ns + "NotasExternas", Notas(data.NotasExternas)));

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

        /// <summary>
        /// Items de una seccion repetible, con UNO EN BLANCO cuando la lista viene vacia.
        ///
        /// Un repeating section de Word dibuja siempre al menos un item: con la lista vacia
        /// muestra el placeholder del control interno, que es el mismo texto en ingles que
        /// aparecia en los campos simples. Verificado en el ticket CAUT-000200328, cuya Cita
        /// no tiene notas y salio con "Click or tap here to enter text." bajo NOTAS.
        ///
        /// La fila en blanco sigue apareciendo —eso lo decide el template, no el XML— pero
        /// sin texto ajeno adentro.
        /// </summary>
        private static IEnumerable<XElement> Trabajos(IReadOnlyList<TicketTrabajo> trabajos)
        {
            if (trabajos.Count == 0)
            {
                yield return new XElement(Ns + "Trabajo",
                    Element("Codigo",             null),
                    Element("DescripcionTrabajo", null));

                yield break;
            }

            foreach (var trabajo in trabajos)
                yield return new XElement(Ns + "Trabajo",
                    Element("Codigo",             trabajo.Codigo),
                    Element("DescripcionTrabajo", trabajo.Descripcion));
        }

        /// <inheritdoc cref="Trabajos"/>
        private static IEnumerable<XElement> Notas(IReadOnlyList<string> notas)
        {
            if (notas.Count == 0)
            {
                yield return new XElement(Ns + "Nota", Element("Texto", null));
                yield break;
            }

            foreach (var nota in notas)
                yield return new XElement(Ns + "Nota", Element("Texto", nota));
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        }

        /// <summary>
        /// Elemento del ticket. Un dato ausente se emite como UN ESPACIO, no como elemento
        /// vacio.
        ///
        /// Emitir el elemento vacio no alcanza: Word muestra el placeholder del content
        /// control ("Click or tap here to enter text.") tanto cuando el nodo bindeado falta
        /// como cuando esta vacio. Verificado en el PDF de la cita -000011, donde FECHA REC,
        /// TELEFONO, MOTOR, CHAPA, ASESOR DE SERVICIO y RAZON SOCIAL salieron con ese texto
        /// en ingles en vez de en blanco.
        ///
        /// El espacio va con xml:space="preserve" para que no lo normalice nadie en el
        /// camino: sin el atributo, un parser que colapse whitespace deja el nodo vacio otra
        /// vez y vuelve el placeholder.
        /// </summary>
        private static XElement Element(string name, string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? new XElement(Ns + name, new XAttribute(XNamespace.Xml + "space", "preserve"), " ")
                : new XElement(Ns + name, value);
    }
}
