using System;

namespace AxxonContacts.Plugins.Models
{
    /// <summary>
    /// Payload JSON que viaja en Azure Service Bus.
    /// Representa un snapshot completo del Contact Raw en el momento del evento.
    ///
    /// Convenciones de naming en JSON:
    ///   - camelCase para todos los campos
    ///   - Campos con alias SQL usan el nombre del alias (ver comentarios)
    ///   - IDs de lookup van como string (Guid) para evitar dependencias de SDK en el consumer
    ///   - Name fields (display names de lookups) van como string nullable
    ///
    /// Sincronizar con AxxonContacts.Functions.Models.ContactEventMessage ante cualquier cambio.
    /// </summary>
    public class ContactEventMessage
    {
        // ── Identidad ────────────────────────────────────────────────
        public string ContactId { get; set; }               // Guid como string

        // ── Datos de persona ─────────────────────────────────────────
        public string FirstName   { get; set; }
        public string MiddleName  { get; set; }
        public string LastName    { get; set; }
        public string MobilePhone   { get; set; }
        public string Description   { get; set; }
        public string EmailAddress1 { get; set; }
        public string EmailAddress2 { get; set; }
        public bool?  MsdynIsProspect { get; set; }

        // ── Control Master/Raw ───────────────────────────────────────
        public bool   IsMaster            { get; set; }
        public string MasterContactId     { get; set; }      // Guid
        public string MasterContactIdName { get; set; }

        // ── Dual Write / F&O ─────────────────────────────────────────
        public string MsdynCompany             { get; set; } // Guid — account
        public string MsdynCompanyName         { get; set; }
        public bool?  MsdynSellable            { get; set; }
        public string MsdynPartyId             { get; set; } // Guid — msdyn_party
        public string MsdynContactPersonId     { get; set; } // alias de msdyn_partyidname
        public string MsdynCustomerGroupId     { get; set; } // Guid — msdyn_customergroup
        public string MsdynCustomerGroupIdName { get; set; }
        public string MsdynIdentificationNumber { get; set; } // clave de matching y SessionId
        public string MsdynPartyCountry        { get; set; }
        public string MsdynPartyStateProvince  { get; set; }
        public string TransactionCurrencyId    { get; set; } // Guid — transactioncurrency
        public string TransactionCurrencyIdName { get; set; }
        public string MsdynPaymentDay          { get; set; } // Guid o OptionSet — verificar metadata
        public string MsdynPaymentDayName      { get; set; }
        public string MsdynPaymentSchedule     { get; set; } // Guid — msdyn_paymentschedule
        public string MsdynCustomerPaymentMethod { get; set; } // alias de msdyn_paymentschedulename
        public string MsdynCustomerPaymentMethodName { get; set; }
        public string MsdynSalesTaxGroup       { get; set; } // Guid — msdyn_salestaxgroup
        public string MsdynSalesTaxGroupName   { get; set; }
        public string MsdynPaymentTerms        { get; set; } // Guid — msdyn_paymentterms
        public string MsdynPaymentTermsName    { get; set; }
        public string MsdynPrimaryContact      { get; set; } // Guid — contact
        public string CreditLimit              { get; set; } // alias de msdyn_primarycontactname

        // ── Custom A365 ──────────────────────────────────────────────
        public int?   A365CreditRating  { get; set; }       // OptionSet
        public bool?  A365OnHoldStatus  { get; set; }
        public string A365Notes         { get; set; }

        // ── Auditoria (solo lectura — no se propagan al Master) ──────
        public string ModifiedBy     { get; set; }          // Guid — systemuser
        public string ModifiedByName { get; set; }
        public string ModifiedOn     { get; set; }          // ISO 8601

        // ── Metadata del evento ──────────────────────────────────────
        public string TriggerMessage { get; set; }          // Create | Update
        public string PublishedAt    { get; set; }          // ISO 8601

        /// <summary>
        /// true cuando msdyn_identificationnumber estaba en el Target del evento
        /// (los campos que cambiaron). En Update indica que el RUC fue establecido
        /// o modificado en esta operacion; dispara la creacion de master en el consumer.
        /// </summary>
        public bool IdentificationNumberChanged { get; set; }
    }
}
