using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>Payload listo para el POST a F&amp;O mas lo que hace falta para la idempotencia.</summary>
    public sealed class FoPayload
    {
        /// <summary>Campos con el nombre de propiedad OData ya resuelto.</summary>
        public required IDictionary<string, object?> Fields { get; init; }

        public required string DataAreaId { get; init; }

        /// <summary>Campo de F&amp;O -&gt; valor, para los campos de <c>key.matchOn</c>.</summary>
        public required IReadOnlyDictionary<string, string?> MatchValues { get; init; }
    }

    /// <summary>
    /// Arma el payload de F&amp;O aplicando un <see cref="EntityMap"/> a un registro de
    /// Dataverse. Toda la logica de mapeo vive aca: los servicios de sync solo orquestan.
    /// </summary>
    public sealed class FoPayloadBuilder
    {
        private readonly IOrganizationService _orgService;
        private readonly IFoSchemaProvider _schema;
        private readonly ILogger<FoPayloadBuilder> _logger;

        public FoPayloadBuilder(
            IOrganizationService orgService,
            IFoSchemaProvider schema,
            ILogger<FoPayloadBuilder> logger)
        {
            _orgService = orgService;
            _schema     = schema;
            _logger     = logger;
        }

        /// <summary>Columnas que hay que traer de Dataverse para aplicar el mapeo.</summary>
        public static ColumnSet ColumnsFor(EntityMap map) => new(map.Columns.ToArray());

        /// <summary>
        /// Evalua las condiciones de <c>syncWhen</c>. Devuelve false y el motivo cuando el
        /// registro no corresponde sincronizarlo (se completa el mensaje sin procesar).
        /// </summary>
        public bool ShouldSync(Entity record, EntityMap map, out string? reason)
        {
            foreach (var condition in map.SyncWhen)
            {
                record.Attributes.TryGetValue(condition.Attribute, out var raw);
                var actual = CrmValue.RenderCanonical(raw);

                if (!string.Equals(actual, condition.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"{condition.Attribute}='{actual ?? "null"}' " +
                             $"(se esperaba '{condition.ExpectedValue}')";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public async Task<FoPayload> BuildAsync(
            Entity record,
            EntityMap map,
            CancellationToken cancellationToken = default)
        {
            var dataAreaId = ResolveDataAreaId(record, map);

            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            var matchValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var companyTarget = await ResolveTargetAsync(map, map.CompanyTargetField, cancellationToken);
            fields[companyTarget] = dataAreaId;

            foreach (var field in map.Fields)
            {
                var value = ResolveValue(record, field);

                if (IsMatchTarget(map, field.TargetField))
                    matchValues[field.TargetField] = CrmValue.RenderCanonical(value);

                if (field.ExcludeFromCreate)
                    continue;

                // Los nulls se omiten para que F&O aplique sus defaults.
                if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                    continue;

                var target = await ResolveTargetAsync(map, field.TargetField, cancellationToken);
                fields[target] = value;
            }

            return new FoPayload
            {
                Fields      = fields,
                DataAreaId  = dataAreaId,
                MatchValues = matchValues
            };
        }

        // ── Resolucion de valores ─────────────────────────────────────

        private object? ResolveValue(Entity record, FieldMap field)
        {
            if (field.Kind == FieldKind.Const)
                return field.ConstantValue;

            if (field.Attribute is null ||
                !record.Attributes.TryGetValue(field.Attribute, out var raw) ||
                raw is null)
                return null;

            return field.Kind switch
            {
                FieldKind.Direct   => CrmValue.ToPayloadValue(raw, field.TargetField),
                FieldKind.Lookup   => ResolveLookup(raw, field),
                FieldKind.Label    => ResolveLabel(record, field, raw),
                FieldKind.ValueMap => ResolveValueMap(field, raw),
                _                  => null
            };
        }

        private object? ResolveLookup(object raw, FieldMap field)
        {
            if (raw is not EntityReference reference)
                throw new NonRetryableSyncException(
                    $"El atributo '{field.Attribute}' esta mapeado como lookup pero su valor es " +
                    $"{raw.GetType().Name}.");

            var related = _orgService.Retrieve(
                reference.LogicalName,
                reference.Id,
                new ColumnSet(field.RelatedAttribute));

            related.Attributes.TryGetValue(field.RelatedAttribute!, out var value);
            return CrmValue.ToPayloadValue(value, field.TargetField);
        }

        private static object? ResolveLabel(Entity record, FieldMap field, object raw) => raw switch
        {
            string text => text,
            OptionSetValue => record.FormattedValues.TryGetValue(field.Attribute!, out var label) ? label : null,
            _ => CrmValue.RenderCanonical(raw)
        };

        private object? ResolveValueMap(FieldMap field, object raw)
        {
            var rendered = CrmValue.RenderCanonical(raw);

            if (rendered is not null &&
                field.ValueMap is not null &&
                field.ValueMap.TryGetValue(rendered, out var mapped))
                return mapped;

            // Un valor sin equivalente en F&O no puede mandarse crudo: se omite el campo.
            _logger.LogWarning(
                "[FoPayloadBuilder] '{Attribute}'='{Value}' no tiene equivalencia en el valueMap " +
                "hacia '{Target}'. Se omite el campo.",
                field.Attribute, rendered ?? "null", field.TargetField);

            return null;
        }

        private string ResolveDataAreaId(Entity record, EntityMap map)
        {
            var companyRef = record.GetAttributeValue<EntityReference>(map.CompanyAttribute);

            if (companyRef is null)
                throw new NonRetryableSyncException(
                    $"El registro {map.SourceEntity} {record.Id} no tiene compania " +
                    $"({map.CompanyAttribute}): no se puede determinar el dataAreaId.");

            var company = _orgService.Retrieve(
                companyRef.LogicalName,
                companyRef.Id,
                new ColumnSet(map.CompanyRelatedField));

            var dataAreaId = company.GetAttributeValue<string>(map.CompanyRelatedField);

            if (string.IsNullOrWhiteSpace(dataAreaId))
                throw new NonRetryableSyncException(
                    $"La compania {companyRef.Id} del registro {record.Id} no tiene " +
                    $"{map.CompanyRelatedField}.");

            return dataAreaId;
        }

        // ── Casing de las propiedades OData ───────────────────────────

        private async Task<string> ResolveTargetAsync(
            EntityMap map,
            string targetField,
            CancellationToken cancellationToken)
        {
            var resolved = await _schema.ResolvePropertyAsync(map.EntitySet, targetField, cancellationToken);

            if (resolved is null)
                throw new NonRetryableSyncException(
                    $"El mapeo '{map.Name}' apunta al campo '{targetField}', que no existe en " +
                    $"{map.EntitySet}. Revisar el export de Dual Write o declarar el nombre correcto " +
                    "en fields del overlay.");

            return resolved;
        }

        private static bool IsMatchTarget(EntityMap map, string targetField) =>
            map.MatchOnTargets.Any(t => string.Equals(t, targetField, StringComparison.OrdinalIgnoreCase));
    }
}
