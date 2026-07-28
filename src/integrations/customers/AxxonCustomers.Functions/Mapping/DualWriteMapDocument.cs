using System.Text.Json.Serialization;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Export de un Table Map de Dual Write, tal cual lo baja el funcional.
    /// Estos archivos NO se editan: el overlay es el que aporta lo nuestro.
    ///
    /// Atencion a la direccion: el export esta escrito AX -&gt; CRM
    /// (<c>sourceField</c> = campo de F&amp;O, <c>destinationField</c> = campo de Dataverse).
    /// Nosotros necesitamos CRM -&gt; AX, asi que el compilador lo lee invertido.
    /// </summary>
    public sealed class DualWriteMapDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("legs")]
        public List<DualWriteLeg> Legs { get; set; } = new();
    }

    public sealed class DualWriteLeg
    {
        /// <summary>Entidad de Dataverse (plural), ej: "contacts".</summary>
        [JsonPropertyName("destinationSchema")]
        public string? DestinationSchema { get; set; }

        /// <summary>Entidad de F&amp;O, ej: "Customers V3".</summary>
        [JsonPropertyName("sourceSchema")]
        public string? SourceSchema { get; set; }

        /// <summary>
        /// Filtro de Dual Write para la direccion CRM -&gt; AX. Se conserva solo como
        /// referencia: la guarda real de la EiP se declara en el overlay
        /// (<c>syncWhen</c>), porque el filtro de Dual Write asume que el customer ya
        /// existe en F&amp;O y nosotros tenemos que crearlo.
        /// </summary>
        [JsonPropertyName("reversedSourceFilter")]
        public string? ReversedSourceFilter { get; set; }

        [JsonPropertyName("fieldMappings")]
        public List<DualWriteFieldMapping> FieldMappings { get; set; } = new();
    }

    public sealed class DualWriteFieldMapping
    {
        /// <summary>Campo de Dataverse. En nuestra direccion es el ORIGEN.</summary>
        [JsonPropertyName("destinationField")]
        public string DestinationField { get; set; } = string.Empty;

        /// <summary>Campo de F&amp;O. En nuestra direccion es el DESTINO.</summary>
        [JsonPropertyName("sourceField")]
        public string SourceField { get; set; } = string.Empty;

        /// <summary>3 = bidireccional. Solo se compilan los bidireccionales.</summary>
        [JsonPropertyName("syncDirection")]
        public int SyncDirection { get; set; }

        [JsonPropertyName("valueTransforms")]
        public List<DualWriteValueTransform>? ValueTransforms { get; set; }
    }

    public sealed class DualWriteValueTransform
    {
        [JsonPropertyName("transformType")]
        public string? TransformType { get; set; }

        /// <summary>Mapa valor-de-AX -&gt; valor-de-CRM. El compilador lo invierte.</summary>
        [JsonPropertyName("valueMap")]
        public Dictionary<string, string>? ValueMap { get; set; }
    }
}
