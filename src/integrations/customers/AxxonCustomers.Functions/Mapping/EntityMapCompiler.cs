using System.Text.Json;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Compila el export de Dual Write (AX -&gt; CRM) mas el overlay en un
    /// <see cref="EntityMap"/> para la direccion CRM -&gt; AX.
    ///
    /// Precedencia: el export define la base -&gt; "ignore" saca filas -&gt; "fields"
    /// corrige o agrega -&gt; "constants" gana siempre.
    ///
    /// Acumula todos los errores y los lanza juntos: corregir un mapeo de a un error por
    /// deploy no es viable.
    /// </summary>
    public static class EntityMapCompiler
    {
        private const int BidirectionalSyncDirection = 3;

        public static EntityMap Compile(
            string name,
            DualWriteMapDocument export,
            MappingOverlayDocument overlay)
        {
            var errors = new List<string>();

            ValidateOverlayShape(overlay, errors);

            var drafts = BuildFromExport(export, overlay, errors);
            ApplyOverlayFields(drafts, overlay, errors);
            ApplyConstants(drafts, overlay, errors);

            var writeBackTarget = ResolveWriteBack(drafts, overlay, errors);
            ValidateMatchOn(drafts, overlay, errors);
            ValidateNoDuplicateTargets(drafts, errors);

            var syncWhen = BuildSyncConditions(overlay, errors);

            if (errors.Count > 0)
                throw new MappingCompilationException(name, errors);

            var fields = drafts.Values
                .Select(d => new FieldMap
                {
                    TargetField       = d.TargetField,
                    Attribute         = d.Attribute,
                    RelatedAttribute  = d.RelatedAttribute,
                    Kind              = d.Kind,
                    ValueMap          = d.ValueMap,
                    ConstantValue     = d.ConstantValue,
                    ExcludeFromCreate = d.ExcludeFromCreate
                })
                .ToList();

            var columns = fields
                .Where(f => f.Attribute is not null)
                .Select(f => f.Attribute!)
                .Concat(new[] { overlay.Company.Attribute, overlay.Key.WriteBack })
                .Concat(syncWhen.Select(c => c.Attribute))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new EntityMap
            {
                Name                 = name,
                SourceEntity         = overlay.Target.SourceEntity,
                EntitySet            = overlay.Target.EntitySet,
                CompanyAttribute     = overlay.Company.Attribute,
                CompanyRelatedField  = overlay.Company.RelatedField,
                CompanyTargetField   = overlay.Company.TargetField,
                WriteBackAttribute   = overlay.Key.WriteBack,
                WriteBackTargetField = writeBackTarget ?? string.Empty,
                MatchOnTargets       = overlay.Key.MatchOn,
                SyncWhen             = syncWhen,
                Fields               = fields,
                Columns              = columns
            };
        }

        // ── Overlay: forma minima ─────────────────────────────────────

        private static void ValidateOverlayShape(MappingOverlayDocument overlay, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(overlay.Target.EntitySet))
                errors.Add("target.entitySet es obligatorio.");
            if (string.IsNullOrWhiteSpace(overlay.Target.SourceEntity))
                errors.Add("target.sourceEntity es obligatorio.");
            if (string.IsNullOrWhiteSpace(overlay.Company.Attribute))
                errors.Add("company.attribute es obligatorio (sin compania no hay dataAreaId).");
            if (string.IsNullOrWhiteSpace(overlay.Company.RelatedField))
                errors.Add("company.relatedField es obligatorio.");
            if (string.IsNullOrWhiteSpace(overlay.Company.TargetField))
                errors.Add("company.targetField es obligatorio.");
            if (string.IsNullOrWhiteSpace(overlay.Key.WriteBack))
                errors.Add("key.writeBack es obligatorio (sin write-back no hay idempotencia).");
            if (overlay.Key.MatchOn.Count == 0)
                errors.Add("key.matchOn no puede estar vacio.");
        }

        // ── Export -> borradores ──────────────────────────────────────

        private static Dictionary<string, DraftField> BuildFromExport(
            DualWriteMapDocument export,
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            var drafts = new Dictionary<string, DraftField>(StringComparer.OrdinalIgnoreCase);
            var ignore = new HashSet<string>(overlay.Ignore, StringComparer.OrdinalIgnoreCase);

            foreach (var leg in export.Legs)
            {
                foreach (var row in leg.FieldMappings)
                {
                    // Una fila unidireccional AX -> CRM no se puede devolver a F&O.
                    if (row.SyncDirection != BidirectionalSyncDirection)
                        continue;

                    var path = (row.DestinationField ?? string.Empty).Trim();
                    if (path.Length == 0)
                    {
                        errors.Add($"Hay una fila del export sin destinationField (sourceField='{row.SourceField}').");
                        continue;
                    }

                    var parts = path.Split('.');
                    if (parts.Length > 2)
                    {
                        errors.Add($"'{path}': solo se admite un nivel de lookup ('atributo.atributoRelacionado').");
                        continue;
                    }

                    var attribute = parts[0];
                    var related   = parts.Length == 2 ? parts[1] : null;

                    if (ignore.Contains(path) || ignore.Contains(attribute))
                        continue;

                    if (string.IsNullOrWhiteSpace(row.SourceField))
                    {
                        errors.Add($"'{path}': la fila del export no tiene sourceField (campo de F&O).");
                        continue;
                    }

                    var draft = new DraftField
                    {
                        Attribute        = attribute,
                        RelatedAttribute = related,
                        TargetField      = row.SourceField.Trim(),
                        Kind             = related is not null ? FieldKind.Lookup : FieldKind.Direct
                    };

                    var valueMap = row.ValueTransforms
                        ?.FirstOrDefault(t => string.Equals(t.TransformType, "ValueMap", StringComparison.OrdinalIgnoreCase))
                        ?.ValueMap;

                    if (valueMap is { Count: > 0 })
                    {
                        draft.ValueMap = Invert(valueMap, path, errors);
                        draft.Kind     = FieldKind.ValueMap;
                    }

                    if (!drafts.TryAdd(path, draft))
                        errors.Add($"'{path}' aparece mas de una vez en el export.");
                }
            }

            return drafts;
        }

        /// <summary>
        /// Invierte el value map de Dual Write (valor-de-AX -&gt; valor-de-CRM) para
        /// nuestra direccion. Si dos valores de AX caen en el mismo valor de CRM la
        /// inversion es ambigua y el mapeo no compila.
        /// </summary>
        private static Dictionary<string, string> Invert(
            Dictionary<string, string> axToCrm,
            string path,
            List<string> errors)
        {
            var inverted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (axValue, crmValue) in axToCrm)
            {
                if (inverted.TryGetValue(crmValue, out var alreadyMapped))
                {
                    errors.Add(
                        $"'{path}': el valueMap no es invertible. El valor de CRM '{crmValue}' " +
                        $"corresponde a '{alreadyMapped}' y a '{axValue}' en F&O. " +
                        "Declarar el mapa explicito en fields del overlay.");
                    continue;
                }

                inverted[crmValue] = axValue;
            }

            return inverted;
        }

        // ── Overlay: correcciones y altas ─────────────────────────────

        private static void ApplyOverlayFields(
            Dictionary<string, DraftField> drafts,
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            foreach (var (key, field) in overlay.Fields)
            {
                var existing = FindDraft(drafts, key, errors);

                if (existing is not null)
                {
                    if (!string.IsNullOrWhiteSpace(field.Target))
                        existing.TargetField = field.Target.Trim();

                    if (!string.IsNullOrWhiteSpace(field.Related))
                        existing.RelatedAttribute = field.Related.Trim();

                    if (field.Map is { Count: > 0 })
                        existing.ValueMap = new Dictionary<string, string>(field.Map, StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(field.Kind))
                    {
                        var kind = ParseKind(field.Kind, key, errors);
                        if (kind is not null)
                            existing.Kind = kind.Value;
                    }

                    ValidateDraft(existing, key, errors);
                    continue;
                }

                // Alta: un campo que el export no trae.
                if (string.IsNullOrWhiteSpace(field.Target))
                {
                    errors.Add($"fields['{key}']: no existe en el export, asi que 'target' es obligatorio.");
                    continue;
                }

                var newKind = ParseKind(field.Kind ?? "direct", key, errors);
                if (newKind is null)
                    continue;

                var draft = new DraftField
                {
                    Attribute        = key,
                    RelatedAttribute = field.Related,
                    TargetField      = field.Target.Trim(),
                    Kind             = newKind.Value,
                    ValueMap         = field.Map is { Count: > 0 }
                                           ? new Dictionary<string, string>(field.Map, StringComparer.OrdinalIgnoreCase)
                                           : null
                };

                if (newKind.Value == FieldKind.Const)
                {
                    if (field.Value is null)
                        errors.Add($"fields['{key}']: kind 'const' requiere 'value'.");
                    else
                        draft.ConstantValue = ReadJson(field.Value.Value, $"fields['{key}'].value", errors);

                    draft.Attribute = null;
                }

                ValidateDraft(draft, key, errors);

                if (!drafts.TryAdd(key, draft))
                    errors.Add($"fields['{key}']: ya hay un mapeo con esa clave.");
            }
        }

        /// <summary>
        /// Busca el borrador por path completo ('msdyn_partyid.msdyn_partynumber') o por
        /// atributo base ('msdyn_partyid') cuando no es ambiguo.
        /// </summary>
        private static DraftField? FindDraft(
            Dictionary<string, DraftField> drafts,
            string key,
            List<string> errors)
        {
            if (drafts.TryGetValue(key, out var exact))
                return exact;

            var byAttribute = drafts
                .Where(kv => string.Equals(kv.Value.Attribute, key, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .ToList();

            if (byAttribute.Count > 1)
            {
                errors.Add(
                    $"fields['{key}']: el atributo esta mapeado {byAttribute.Count} veces en el export. " +
                    "Usar el path completo 'atributo.atributoRelacionado'.");
                return null;
            }

            return byAttribute.SingleOrDefault();
        }

        private static void ApplyConstants(
            Dictionary<string, DraftField> drafts,
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            foreach (var (targetField, element) in overlay.Constants)
            {
                // Una constante gana sobre cualquier fila del export con el mismo destino.
                var overridden = drafts
                    .Where(kv => string.Equals(kv.Value.TargetField, targetField, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in overridden)
                    drafts.Remove(key);

                drafts[$"const:{targetField}"] = new DraftField
                {
                    TargetField   = targetField,
                    Kind          = FieldKind.Const,
                    ConstantValue = ReadJson(element, $"constants['{targetField}']", errors)
                };
            }
        }

        // ── Validaciones cruzadas ─────────────────────────────────────

        private static string? ResolveWriteBack(
            Dictionary<string, DraftField> drafts,
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(overlay.Key.WriteBack))
                return null;

            var draft = drafts.Values.FirstOrDefault(d =>
                string.Equals(d.Attribute, overlay.Key.WriteBack, StringComparison.OrdinalIgnoreCase));

            if (draft is null)
            {
                errors.Add(
                    $"key.writeBack '{overlay.Key.WriteBack}' no esta mapeado. " +
                    "Tiene que existir en el export o en fields para saber con que campo de F&O se corresponde.");
                return null;
            }

            // No se manda en el POST: F&O genera el CustomerAccount por number sequence.
            draft.ExcludeFromCreate = true;
            return draft.TargetField;
        }

        private static void ValidateMatchOn(
            Dictionary<string, DraftField> drafts,
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            foreach (var target in overlay.Key.MatchOn)
            {
                var mapped = drafts.Values.Any(d =>
                    string.Equals(d.TargetField, target, StringComparison.OrdinalIgnoreCase));

                if (!mapped)
                    errors.Add(
                        $"key.matchOn '{target}': ningun campo del mapeo apunta a ese campo de F&O. " +
                        "Sin valor no se puede verificar si el registro ya existe.");
            }
        }

        private static void ValidateNoDuplicateTargets(
            Dictionary<string, DraftField> drafts,
            List<string> errors)
        {
            var duplicates = drafts.Values
                .GroupBy(d => d.TargetField, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
                errors.Add(
                    $"El campo de F&O '{group.Key}' esta mapeado {group.Count()} veces " +
                    $"(desde: {string.Join(", ", group.Select(d => d.Attribute ?? "constante"))}).");
        }

        private static List<SyncCondition> BuildSyncConditions(
            MappingOverlayDocument overlay,
            List<string> errors)
        {
            var conditions = new List<SyncCondition>();

            foreach (var condition in overlay.SyncWhen)
            {
                if (string.IsNullOrWhiteSpace(condition.Attribute))
                {
                    errors.Add("syncWhen: hay una condicion sin 'attribute'.");
                    continue;
                }

                var expected = CrmValue.RenderCanonical(
                    ReadJson(condition.ExpectedValue, $"syncWhen['{condition.Attribute}']", errors));

                if (expected is null)
                {
                    errors.Add($"syncWhen['{condition.Attribute}']: 'equals' no puede ser null.");
                    continue;
                }

                conditions.Add(new SyncCondition
                {
                    Attribute     = condition.Attribute,
                    ExpectedValue = expected
                });
            }

            return conditions;
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static void ValidateDraft(DraftField draft, string key, List<string> errors)
        {
            switch (draft.Kind)
            {
                case FieldKind.Lookup when string.IsNullOrWhiteSpace(draft.RelatedAttribute):
                    errors.Add($"fields['{key}']: kind 'lookup' requiere 'related' (o un path 'atributo.atributoRelacionado').");
                    break;

                case FieldKind.ValueMap when draft.ValueMap is null or { Count: 0 }:
                    errors.Add($"fields['{key}']: kind 'valueMap' requiere 'map'.");
                    break;

                case FieldKind.Const when draft.Attribute is not null && draft.ConstantValue is null:
                    errors.Add($"fields['{key}']: kind 'const' requiere 'value'.");
                    break;
            }
        }

        private static FieldKind? ParseKind(string kind, string key, List<string> errors)
        {
            switch (kind.Trim().ToLowerInvariant())
            {
                case "direct":   return FieldKind.Direct;
                case "lookup":   return FieldKind.Lookup;
                case "valuemap": return FieldKind.ValueMap;
                case "label":    return FieldKind.Label;
                case "const":    return FieldKind.Const;

                default:
                    errors.Add(
                        $"fields['{key}']: kind '{kind}' desconocido. " +
                        "Admitidos: direct, lookup, valueMap, label, const.");
                    return null;
            }
        }

        private static object? ReadJson(JsonElement element, string context, List<string> errors)
        {
            try
            {
                return CrmValue.FromJson(element, context);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
                return null;
            }
        }

        private sealed class DraftField
        {
            public string? Attribute;
            public string? RelatedAttribute;
            public string TargetField = string.Empty;
            public FieldKind Kind;
            public Dictionary<string, string>? ValueMap;
            public object? ConstantValue;
            public bool ExcludeFromCreate;
        }
    }
}
