using System.Text.Json;
using AxxonCustomers.Functions.Mapping;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// Armado de documentos de mapeo para los tests. La base es valida: cada test rompe
    /// o extiende solo lo que esta probando.
    /// </summary>
    internal static class Given
    {
        public const string WriteBackAttribute = "accountnumber";
        public const string WriteBackTarget    = "CUSTOMERACCOUNT";

        /// <summary>Fila del export (direccion AX -&gt; CRM, como la escribe Dual Write).</summary>
        public static DualWriteFieldMapping Row(
            string crmField,
            string foField,
            Dictionary<string, string>? valueMap = null,
            int syncDirection = 3) =>
            new()
            {
                DestinationField = crmField,
                SourceField      = foField,
                SyncDirection    = syncDirection,
                ValueTransforms  = valueMap is null
                    ? null
                    : new List<DualWriteValueTransform>
                    {
                        new() { TransformType = "ValueMap", ValueMap = valueMap }
                    }
            };

        public static DualWriteMapDocument Export(params DualWriteFieldMapping[] rows) =>
            new()
            {
                Legs = { new DualWriteLeg { FieldMappings = rows.ToList() } }
            };

        /// <summary>Export minimo valido: solo la fila del write-back.</summary>
        public static DualWriteMapDocument ExportWith(params DualWriteFieldMapping[] rows) =>
            Export(new[] { Row(WriteBackAttribute, WriteBackTarget) }.Concat(rows).ToArray());

        public static MappingOverlayDocument Overlay() =>
            new()
            {
                Target  = new OverlayTarget { EntitySet = "CustomersV3", SourceEntity = "account" },
                Company = new OverlayCompany
                {
                    Attribute    = "msdyn_company",
                    RelatedField = "cdm_companycode",
                    TargetField  = "dataAreaId"
                },
                Key = new OverlayKey
                {
                    WriteBack = WriteBackAttribute,
                    MatchOn   = { WriteBackTarget }
                }
            };

        public static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

        public static EntityMap Compile(DualWriteMapDocument export, MappingOverlayDocument overlay) =>
            EntityMapCompiler.Compile("test", export, overlay);

        public static IReadOnlyList<string> CompileErrors(
            DualWriteMapDocument export,
            MappingOverlayDocument overlay)
        {
            var ex = Assert.Throws<MappingCompilationException>(() => Compile(export, overlay));
            return ex.Errors;
        }

        public static FieldMap Field(this EntityMap map, string targetField) =>
            map.Fields.Single(f => string.Equals(f.TargetField, targetField, StringComparison.OrdinalIgnoreCase));
    }
}
