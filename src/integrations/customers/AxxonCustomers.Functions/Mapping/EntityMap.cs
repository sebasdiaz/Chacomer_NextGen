namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Como se obtiene el valor de un campo. Cinco primitivas, cerradas a proposito:
    /// lo que no entre aca se resuelve en C# con nombre propio y tests, no en un JSON
    /// sin debugger.
    /// </summary>
    public enum FieldKind
    {
        /// <summary>Valor del atributo tal cual (string, int, decimal, bool, Money, fecha).</summary>
        Direct,

        /// <summary>Lookup: se recupera <c>RelatedAttribute</c> de la entidad relacionada.</summary>
        Lookup,

        /// <summary>Se renderiza el valor a string canonico y se busca en <c>ValueMap</c>.</summary>
        ValueMap,

        /// <summary>Texto si el atributo es string; etiqueta formateada si es OptionSet.</summary>
        Label,

        /// <summary>Valor fijo declarado en el overlay.</summary>
        Const
    }

    /// <summary>Un campo del payload de F&amp;O y de donde sale.</summary>
    public sealed class FieldMap
    {
        /// <summary>
        /// Nombre del campo en F&amp;O tal como viene del export (MAYUSCULAS, ej:
        /// "PARTYNUMBER"). El casing real de la propiedad OData lo resuelve
        /// <see cref="IFoSchemaProvider"/> contra el environment.
        /// </summary>
        public required string TargetField { get; init; }

        /// <summary>Atributo de Dataverse. Null solo para <see cref="FieldKind.Const"/>.</summary>
        public string? Attribute { get; init; }

        /// <summary>Atributo de la entidad relacionada (solo <see cref="FieldKind.Lookup"/>).</summary>
        public string? RelatedAttribute { get; init; }

        public required FieldKind Kind { get; init; }

        /// <summary>Valor renderizado de CRM -&gt; valor de F&amp;O.</summary>
        public IReadOnlyDictionary<string, string>? ValueMap { get; init; }

        public object? ConstantValue { get; init; }

        /// <summary>
        /// El campo de write-back no se manda en el POST (F&amp;O genera el
        /// CustomerAccount por number sequence), pero se conserva en el mapa porque
        /// aporta el valor con el que se busca el registro existente.
        /// </summary>
        public bool ExcludeFromCreate { get; init; }
    }

    /// <summary>Condicion que el registro de Dataverse debe cumplir para sincronizarse.</summary>
    public sealed class SyncCondition
    {
        public required string Attribute { get; init; }

        /// <summary>Valor esperado, ya renderizado a string canonico.</summary>
        public required string ExpectedValue { get; init; }
    }

    /// <summary>
    /// Mapeo compilado de una entidad: export de Dual Write invertido + overlay,
    /// ya validado. Es inmutable y se arma una sola vez al arranque.
    /// </summary>
    public sealed class EntityMap
    {
        /// <summary>Nombre del mapeo para logs y lookup en el registry (ej: "contact").</summary>
        public required string Name { get; init; }

        /// <summary>Logical name de Dataverse.</summary>
        public required string SourceEntity { get; init; }

        /// <summary>Entity set de la OData API de F&amp;O.</summary>
        public required string EntitySet { get; init; }

        public required string CompanyAttribute { get; init; }
        public required string CompanyRelatedField { get; init; }
        public required string CompanyTargetField { get; init; }

        /// <summary>Atributo de Dataverse que recibe el CustomerAccount generado.</summary>
        public required string WriteBackAttribute { get; init; }

        /// <summary>Campo de F&amp;O correspondiente al write-back (ej: "CUSTOMERACCOUNT").</summary>
        public required string WriteBackTargetField { get; init; }

        /// <summary>Campos de F&amp;O con los que se busca el registro existente.</summary>
        public required IReadOnlyList<string> MatchOnTargets { get; init; }

        public required IReadOnlyList<SyncCondition> SyncWhen { get; init; }

        public required IReadOnlyList<FieldMap> Fields { get; init; }

        /// <summary>ColumnSet del Retrieve: todo lo que el mapeo necesita leer.</summary>
        public required IReadOnlyList<string> Columns { get; init; }
    }
}
