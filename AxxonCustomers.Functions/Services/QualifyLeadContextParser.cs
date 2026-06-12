using System.Text.Json;
using AxxonCustomers.Functions.Models;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Parsea el RemoteExecutionContext nativo que el Service Endpoint de Dataverse
    /// publica en la cola ante cada QualifyLead.
    ///
    /// Del payload solo interesa InputParameters:
    ///   - "OpportunityCustomerId" : EntityReference al contact creado/asociado al calificar.
    ///   - "LeadId"                : EntityReference al lead calificado (para logging).
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

            if (!root.TryGetProperty("InputParameters", out var inputParams)
                || inputParams.ValueKind != JsonValueKind.Array)
                return context;

            foreach (var param in inputParams.EnumerateArray())
            {
                if (!param.TryGetProperty("key", out var keyEl))
                    continue;

                var key = keyEl.GetString();

                if (string.Equals(key, "OpportunityCustomerId", StringComparison.OrdinalIgnoreCase))
                {
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

            return context;
        }

        // EntityReference: { "value": { "Id": "guid", "LogicalName": "contact", ... } }
        private static (Guid? Id, string? LogicalName) ReadEntityReference(JsonElement param)
        {
            if (!param.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Object)
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
