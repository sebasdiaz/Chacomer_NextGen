using System.Text.Json;
using AxxonContacts.Functions.Models;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Parsea el RemoteExecutionContext de Dataverse para la entidad Account.
    /// Fusiona Target + preImage (Target gana en colision) y devuelve un AccountEventMessage.
    /// </summary>
    public static class AccountExecutionContextParser
    {
        public static AccountEventMessage Parse(string raw)
        {
            // Dataverse puede envolver el body con framing AMQP (byte 0x40 = '@').
            // Extraemos el JSON real buscando el primer '{'.
            var jsonStart = raw.IndexOf('{');
            var json = jsonStart >= 0 ? raw[jsonStart..] : raw;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var messageName = root.GetProperty("MessageName").GetString() ?? string.Empty;

            if (!Guid.TryParse(root.GetProperty("PrimaryEntityId").GetString(), out var accountId))
                throw new InvalidOperationException("PrimaryEntityId no es un Guid valido.");

            var targetAttrs   = ExtractAttributes(root, "InputParameters", "Target");
            var preImageAttrs = ExtractAttributes(root, "PreEntityImages",  "preImage");

            bool identificationChanged = targetAttrs.ContainsKey("msdyn_identificationnumber");

            var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in preImageAttrs) merged[k] = v;
            foreach (var (k, v) in targetAttrs)   merged[k] = v;

            return new AccountEventMessage
            {
                AccountId                  = accountId,
                TriggerMessage             = messageName,
                PublishedAt                = DateTimeOffset.UtcNow,
                IdentificationNumberChanged = identificationChanged,

                MsdynIdentificationNumber = Str(merged, "msdyn_identificationnumber"),
                IsMaster                  = Bool(merged, "axx_ismaster") ?? false,
                MasterAccountId           = Ref(merged,  "axx_masteraccountid"),

                Name          = Str(merged, "name"),
                Telephone1    = Str(merged, "telephone1"),
                EmailAddress1 = Str(merged, "emailaddress1"),
                Description   = Str(merged, "description"),

                ModifiedBy = Ref(merged, "modifiedby"),
            };
        }

        // ── Extraccion de atributos ───────────────────────────────────

        private static Dictionary<string, JsonElement> ExtractAttributes(
            JsonElement root, string collectionName, string entryKey)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty(collectionName, out var collection)
                || collection.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in collection.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var keyEl)
                    || !string.Equals(keyEl.GetString(), entryKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!item.TryGetProperty("value", out var valueEl)
                    || !valueEl.TryGetProperty("Attributes", out var attrs)
                    || attrs.ValueKind != JsonValueKind.Array)
                    break;

                foreach (var attr in attrs.EnumerateArray())
                {
                    if (!attr.TryGetProperty("key",   out var attrKey)
                        || !attr.TryGetProperty("value", out var attrVal)
                        || attrVal.ValueKind == JsonValueKind.Null)
                        continue;

                    var name = attrKey.GetString();
                    if (!string.IsNullOrEmpty(name))
                        result[name] = attrVal;
                }
                break;
            }

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static string? Str(Dictionary<string, JsonElement> m, string key)
            => m.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        private static bool? Bool(Dictionary<string, JsonElement> m, string key)
        {
            if (!m.TryGetValue(key, out var el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.True  => true,
                JsonValueKind.False => false,
                _                   => null
            };
        }

        private static Guid? Ref(Dictionary<string, JsonElement> m, string key)
        {
            if (!m.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty("Id", out var idEl)) return null;
            return Guid.TryParse(idEl.GetString(), out var g) ? g : null;
        }
    }
}
