using System.Text.Json;
using AxxonCustomers.Functions.Models;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Parsea el RemoteExecutionContext nativo que el Service Endpoint de Dataverse
    /// publica en la cola ante cada QualifyLead.
    ///
    /// Del payload interesan:
    ///   - InputParameters."OpportunityCustomerId" : EntityReference al cliente asociado
    ///     a la opportunity. Solo trae valor cuando el caller lo pasa explicitamente;
    ///     en el flujo estandar de la UI viene null (o referencia al account en leads B2B).
    ///   - OutputParameters."CreatedEntities" : EntityReferenceCollection del
    ///     QualifyLeadResponse con los registros creados al calificar (account,
    ///     contact, opportunity). De aqui se toma el contact cuando
    ///     OpportunityCustomerId no lo trae.
    ///   - InputParameters."LeadId" : EntityReference al lead calificado (para logging).
    /// </summary>
    public static class QualifyLeadContextParser
    {
        public static QualifyLeadContext Parse(string raw)
        {
            // El body puede traer un prefijo binario segun el formato del endpoint;
            // se busca el inicio del JSON igual que en AxxonContacts.Functions.
            var jsonStart = raw.IndexOf('{');
            var json = jsonStart >= 0 ? raw[jsonStart..] : raw;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var context = new QualifyLeadContext
            {
                MessageName = root.TryGetProperty("MessageName", out var msgEl)
                    ? msgEl.GetString() ?? string.Empty
                    : string.Empty
            };

            if (root.TryGetProperty("InputParameters", out var inputParams)
                && inputParams.ValueKind == JsonValueKind.Array)
            {
                foreach (var param in inputParams.EnumerateArray())
                {
                    if (!param.TryGetProperty("key", out var keyEl))
                        continue;

                    var key = keyEl.GetString();

                    if (string.Equals(key, "OpportunityCustomerId", StringComparison.OrdinalIgnoreCase))
                    {
                        context.HasOpportunityCustomerId = true;

                        var (id, logicalName) = ReadEntityReference(param);
                        context.CustomerLogicalName = logicalName;

                        if (string.Equals(logicalName, "contact", StringComparison.OrdinalIgnoreCase))
                            context.ContactId = id;
                    }
                    else if (string.Equals(key, "LeadId", StringComparison.OrdinalIgnoreCase))
                    {
                        var (id, _) = ReadEntityReference(param);
                        context.LeadId = id;
                    }
                }
            }

            // Flujo estandar de la UI: OpportunityCustomerId viene null (o es el
            // account en leads B2B). El contact creado al calificar llega en
            // OutputParameters.CreatedEntities (QualifyLeadResponse).
            var createdContact = FindCreatedContact(root, context);
            context.ContactId ??= createdContact;

            return context;
        }

        private static Guid? FindCreatedContact(JsonElement root, QualifyLeadContext context)
        {
            if (!root.TryGetProperty("OutputParameters", out var outputParams)
                || outputParams.ValueKind != JsonValueKind.Array)
                return null;

            Guid? contactId = null;

            foreach (var param in outputParams.EnumerateArray())
            {
                if (!param.TryGetProperty("key", out var keyEl)
                    || !string.Equals(keyEl.GetString(), "CreatedEntities", StringComparison.OrdinalIgnoreCase))
                    continue;

                context.HasCreatedEntities = true;

                if (!param.TryGetProperty("value", out var refs)
                    || refs.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var reference in refs.EnumerateArray())
                {
                    var (id, logicalName) = ReadReference(reference);

                    if (!string.IsNullOrEmpty(logicalName))
                        context.CreatedEntityLogicalNames.Add(logicalName);

                    if (contactId == null
                        && id.HasValue
                        && string.Equals(logicalName, "contact", StringComparison.OrdinalIgnoreCase))
                        contactId = id;
                }
            }

            return contactId;
        }

        // Entrada {key, value} cuyo value es un EntityReference
        private static (Guid? Id, string? LogicalName) ReadEntityReference(JsonElement param)
        {
            if (!param.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Object)
                return (null, null);

            return ReadReference(value);
        }

        // EntityReference: { "Id": "guid", "LogicalName": "contact", ... }
        private static (Guid? Id, string? LogicalName) ReadReference(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                return (null, null);

            Guid? id = null;
            if (value.TryGetProperty("Id", out var idEl)
                && Guid.TryParse(idEl.GetString(), out var parsed))
                id = parsed;

            string? logicalName = null;
            if (value.TryGetProperty("LogicalName", out var lnEl)
                && lnEl.ValueKind == JsonValueKind.String)
                logicalName = lnEl.GetString();

            return (id, logicalName);
        }
    }
}
