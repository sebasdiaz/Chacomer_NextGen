using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Lo que el esquema de Dual Write no puede expresar para la direccion CRM -&gt; AX:
    /// dataAreaId, clave de upsert, campo de write-back, constantes, guarda de
    /// sincronizacion y correcciones sobre filas del export.
    ///
    /// Admite comentarios (se cargan con <c>ReadCommentHandling.Skip</c>): un overlay sin
    /// el "por que" de cada correccion es imposible de revisar en un PR.
    /// </summary>
    public sealed class MappingOverlayDocument
    {
        [JsonPropertyName("target")]
        public OverlayTarget Target { get; set; } = new();

        [JsonPropertyName("company")]
        public OverlayCompany Company { get; set; } = new();

        [JsonPropertyName("key")]
        public OverlayKey Key { get; set; } = new();

        /// <summary>Condiciones que el registro debe cumplir para sincronizarse (AND).</summary>
        [JsonPropertyName("syncWhen")]
        public List<OverlayCondition> SyncWhen { get; set; } = new();

        /// <summary>Atributos de Dataverse del export que se descartan.</summary>
        [JsonPropertyName("ignore")]
        public List<string> Ignore { get; set; } = new();

        /// <summary>Campo de F&amp;O -&gt; valor fijo. Gana sobre cualquier fila del export.</summary>
        [JsonPropertyName("constants")]
        public Dictionary<string, JsonElement> Constants { get; set; } = new();

        /// <summary>Atributo de Dataverse -&gt; correccion o alta de mapeo.</summary>
        [JsonPropertyName("fields")]
        public Dictionary<string, OverlayField> Fields { get; set; } = new();
    }

    public sealed class OverlayTarget
    {
        /// <summary>Entity set de la OData API de F&amp;O, ej: "CustomersV3".</summary>
        [JsonPropertyName("entitySet")]
        public string EntitySet { get; set; } = string.Empty;

        /// <summary>Logical name de Dataverse, ej: "contact".</summary>
        [JsonPropertyName("sourceEntity")]
        public string SourceEntity { get; set; } = string.Empty;
    }

    public sealed class OverlayCompany
    {
        [JsonPropertyName("attribute")]
        public string Attribute { get; set; } = string.Empty;

        [JsonPropertyName("relatedField")]
        public string RelatedField { get; set; } = string.Empty;

        [JsonPropertyName("targetField")]
        public string TargetField { get; set; } = string.Empty;
    }

    public sealed class OverlayKey
    {
        /// <summary>Atributo de Dataverse que recibe el CustomerAccount que genera F&amp;O.</summary>
        [JsonPropertyName("writeBack")]
        public string WriteBack { get; set; } = string.Empty;

        /// <summary>Campos de F&amp;O con los que se busca el registro existente.</summary>
        [JsonPropertyName("matchOn")]
        public List<string> MatchOn { get; set; } = new();

        /// <summary>
        /// Campos de F&amp;O que no viajan en el PATCH. Los de <see cref="MatchOn"/> y el
        /// <c>dataAreaId</c> ya se excluyen solos (son la clave del registro); aca van los
        /// que F&amp;O no deja cambiar una vez creado el customer, ej: <c>PartyType</c>.
        /// </summary>
        [JsonPropertyName("immutable")]
        public List<string> Immutable { get; set; } = new();
    }

    public sealed class OverlayCondition
    {
        [JsonPropertyName("attribute")]
        public string Attribute { get; set; } = string.Empty;

        [JsonPropertyName("equals")]
        public JsonElement ExpectedValue { get; set; }
    }

    public sealed class OverlayField
    {
        /// <summary>Campo de F&amp;O. Si se omite, se hereda la fila del export.</summary>
        [JsonPropertyName("target")]
        public string? Target { get; set; }

        /// <summary>direct | lookup | valueMap | label | const.</summary>
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        /// <summary>Atributo de la entidad relacionada (solo para <c>lookup</c>).</summary>
        [JsonPropertyName("related")]
        public string? Related { get; set; }

        /// <summary>Valor renderizado de CRM -&gt; valor de F&amp;O (solo para <c>valueMap</c>).</summary>
        [JsonPropertyName("map")]
        public Dictionary<string, string>? Map { get; set; }

        /// <summary>Valor fijo (solo para <c>const</c>).</summary>
        [JsonPropertyName("value")]
        public JsonElement? Value { get; set; }
    }
}
