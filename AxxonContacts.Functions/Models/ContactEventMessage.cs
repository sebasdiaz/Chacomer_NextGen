using System;
using System.Text.Json.Serialization;

namespace AxxonContacts.Functions.Models
{
    /// <summary>
    /// Payload JSON que llega desde el plugin via Azure Service Bus.
    /// Snapshot completo del Contact Raw en el momento del evento.
    ///
    /// Sincronizar con AxxonContacts.Plugins.Models.ContactEventMessage ante cualquier cambio.
    /// JsonPropertyName mapea los nombres camelCase del JSON a las propiedades PascalCase de C#.
    /// </summary>
    public class ContactEventMessage
    {
        // ── Identidad ────────────────────────────────────────────────
        [JsonPropertyName("contactId")]
        public Guid ContactId { get; set; }

        // ── Metadata del evento ──────────────────────────────────────
        [JsonPropertyName("triggerMessage")]
        public string TriggerMessage { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset PublishedAt { get; set; }

        // ── Datos de persona ─────────────────────────────────────────
        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("middleName")]
        public string? MiddleName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("mobilePhone")]
        public string? MobilePhone { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("emailAddress1")]
        public string? EmailAddress1 { get; set; }

        [JsonPropertyName("emailAddress2")]
        public string? EmailAddress2 { get; set; }

        [JsonPropertyName("msdynIsProspect")]
        public bool? MsdynIsProspect { get; set; }

        // ── Control Master/Raw ───────────────────────────────────────
        [JsonPropertyName("isMaster")]
        public bool IsMaster { get; set; }

        [JsonPropertyName("masterContactId")]
        public Guid? MasterContactId { get; set; }

        [JsonPropertyName("masterContactIdName")]
        public string? MasterContactIdName { get; set; }

        // ── Dual Write / F&O ─────────────────────────────────────────
        [JsonPropertyName("msdynCompany")]
        public Guid? MsdynCompany { get; set; }

        [JsonPropertyName("msdynCompanyName")]
        public string? MsdynCompanyName { get; set; }

        [JsonPropertyName("msdynSellable")]
        public bool? MsdynSellable { get; set; }

        [JsonPropertyName("msdynPartyId")]
        public Guid? MsdynPartyId { get; set; }

        /// <summary>Alias de msdyn_partyidname en el modelo de datos.</summary>
        [JsonPropertyName("msdynContactPersonId")]
        public string? MsdynContactPersonId { get; set; }

        [JsonPropertyName("msdynCustomerGroupId")]
        public Guid? MsdynCustomerGroupId { get; set; }

        [JsonPropertyName("msdynCustomerGroupIdName")]
        public string? MsdynCustomerGroupIdName { get; set; }

        /// <summary>Clave de matching. Coincide con el SessionId de Service Bus.</summary>
        [JsonPropertyName("msdynIdentificationNumber")]
        public string? MsdynIdentificationNumber { get; set; }

        [JsonPropertyName("msdynPartyCountry")]
        public string? MsdynPartyCountry { get; set; }

        [JsonPropertyName("msdynPartyStateProvince")]
        public string? MsdynPartyStateProvince { get; set; }

        [JsonPropertyName("transactionCurrencyId")]
        public Guid? TransactionCurrencyId { get; set; }

        [JsonPropertyName("transactionCurrencyIdName")]
        public string? TransactionCurrencyIdName { get; set; }

        /// <summary>
        /// msdyn_paymentday: puede ser Lookup (Guid) u OptionSet segun el environment.
        /// Se deserializa como string para manejar ambos casos. Verificar metadata.
        /// </summary>
        [JsonPropertyName("msdynPaymentDay")]
        public string? MsdynPaymentDay { get; set; }

        [JsonPropertyName("msdynPaymentDayName")]
        public string? MsdynPaymentDayName { get; set; }

        [JsonPropertyName("msdynPaymentSchedule")]
        public Guid? MsdynPaymentSchedule { get; set; }

        /// <summary>Alias de msdyn_paymentschedulename en el modelo de datos.</summary>
        [JsonPropertyName("msdynCustomerPaymentMethod")]
        public string? MsdynCustomerPaymentMethod { get; set; }

        [JsonPropertyName("msdynCustomerPaymentMethodName")]
        public string? MsdynCustomerPaymentMethodName { get; set; }

        [JsonPropertyName("msdynSalesTaxGroup")]
        public Guid? MsdynSalesTaxGroup { get; set; }

        [JsonPropertyName("msdynSalesTaxGroupName")]
        public string? MsdynSalesTaxGroupName { get; set; }

        [JsonPropertyName("msdynPaymentTerms")]
        public Guid? MsdynPaymentTerms { get; set; }

        [JsonPropertyName("msdynPaymentTermsName")]
        public string? MsdynPaymentTermsName { get; set; }

        [JsonPropertyName("msdynPrimaryContact")]
        public Guid? MsdynPrimaryContact { get; set; }

        /// <summary>Alias de msdyn_primarycontactname en el modelo de datos.</summary>
        [JsonPropertyName("creditLimit")]
        public string? CreditLimit { get; set; }

        // ── Custom A365 ──────────────────────────────────────────────
        [JsonPropertyName("a365CreditRating")]
        public int? A365CreditRating { get; set; }

        [JsonPropertyName("a365OnHoldStatus")]
        public bool? A365OnHoldStatus { get; set; }

        [JsonPropertyName("a365Notes")]
        public string? A365Notes { get; set; }

        // ── Auditoria (no se propagan al Master) ─────────────────────
        [JsonPropertyName("modifiedBy")]
        public Guid? ModifiedBy { get; set; }

        [JsonPropertyName("modifiedByName")]
        public string? ModifiedByName { get; set; }

        [JsonPropertyName("modifiedOn")]
        public DateTimeOffset? ModifiedOn { get; set; }
    }
}
