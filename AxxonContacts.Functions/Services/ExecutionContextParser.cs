using System.Text.Json;
using AxxonContacts.Functions.Models;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Parsea el RemoteExecutionContext nativo que Dataverse publica via Service Endpoint.
    /// Fusiona Target (campos que cambiaron) + preImage (estado previo completo) —
    /// Target gana en caso de colision — y devuelve un ContactEventMessage interno.
    /// </summary>
    public static class ExecutionContextParser
    {
        public static ContactEventMessage Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var messageName = root.GetProperty("MessageName").GetString() ?? string.Empty;

            if (!Guid.TryParse(root.GetProperty("PrimaryEntityId").GetString(), out var contactId))
                throw new InvalidOperationException("PrimaryEntityId no es un Guid valido.");

            var targetAttrs   = ExtractAttributes(root, "InputParameters", "Target");
            var preImageAttrs = ExtractAttributes(root, "PreEntityImages",  "preImage");

            // true cuando msdyn_identificationnumber esta entre los campos que cambiaron (Target).
            // Para eventos Update esto indica que el RUC fue establecido en esta operacion.
            bool identificationChanged = targetAttrs.ContainsKey("msdyn_identificationnumber");

            // PreImage provee la base; Target sobreescribe con los campos que cambiaron
            var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in preImageAttrs) merged[k] = v;
            foreach (var (k, v) in targetAttrs)   merged[k] = v;

            return new ContactEventMessage
            {
                ContactId                  = contactId,
                TriggerMessage             = messageName,
                PublishedAt                = DateTimeOffset.UtcNow,
                IdentificationNumberChanged = identificationChanged,

                MsdynIdentificationNumber = Str(merged, "msdyn_identificationnumber"),
                IsMaster                = Bool(merged, "axx_ismaster") ?? false,
                MasterContactId         = Ref(merged,  "axx_mastercontactid"),

                FirstName               = Str(merged, "firstname"),
                MiddleName              = Str(merged, "middlename"),
                LastName                = Str(merged, "lastname"),
                MobilePhone             = Str(merged, "mobilephone"),
                Description             = Str(merged, "description"),
                EmailAddress1           = Str(merged, "emailaddress1"),
                EmailAddress2           = Str(merged, "emailaddress2"),
                MsdynIsProspect         = Bool(merged, "msdyn_isprospect"),

                MsdynCompany            = Ref(merged, "msdyn_company"),
                MsdynPartyId            = Ref(merged, "msdyn_partyid"),
                MsdynCustomerGroupId    = Ref(merged, "msdyn_customergroupid"),
                TransactionCurrencyId   = Ref(merged, "transactioncurrencyid"),
                MsdynPaymentSchedule    = Ref(merged, "msdyn_paymentschedule"),
                MsdynSalesTaxGroup      = Ref(merged, "msdyn_salestaxgroup"),
                MsdynPaymentTerms       = Ref(merged, "msdyn_paymentterms"),
                MsdynPrimaryContact     = Ref(merged, "msdyn_primarycontact"),
                MsdynSellable           = Bool(merged, "msdyn_sellable"),
                MsdynPartyCountry       = Str(merged, "msdyn_partycountry"),
                MsdynPartyStateProvince = Str(merged, "msdyn_partystateprovince"),

                // msdyn_paymentday puede ser Lookup o OptionSet segun el environment
                MsdynPaymentDay         = Ref(merged, "msdyn_paymentday")?.ToString()
                                          ?? Osv(merged, "msdyn_paymentday")?.ToString(),

                A365CreditRating        = Osv(merged,  "a365_creditrating"),
                A365OnHoldStatus        = Bool(merged, "a365_onholdstatus"),
                A365Notes               = Str(merged,  "a365_notes"),
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

        // ── Helpers de extraccion de valores ─────────────────────────

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

        // EntityReference: { "__type": "EntityReference:...", "Id": "guid", ... }
        private static Guid? Ref(Dictionary<string, JsonElement> m, string key)
        {
            if (!m.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty("Id", out var idEl)) return null;
            return Guid.TryParse(idEl.GetString(), out var g) ? g : null;
        }

        // OptionSetValue: { "__type": "OptionSetValue:...", "Value": 0 }
        private static int? Osv(Dictionary<string, JsonElement> m, string key)
        {
            if (!m.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty("Value", out var valEl)) return null;
            return valEl.ValueKind == JsonValueKind.Number ? valEl.GetInt32() : null;
        }
    }
}
