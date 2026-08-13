using System.Collections.Generic;

namespace AxxonContacts.Plugins.Constants
{
    /// <summary>
    /// Nombres logicos de campos de Dataverse y metadata del solution.
    /// Unica fuente de verdad para evitar magic strings.
    /// </summary>
    public static class ContactConstants
    {
        public const string EntityLogicalName = "contact";

        // ── Custom Axxon ────────────────────────────────────────────
        public const string IsMaster = "axx_ismaster";

        // ── Custom Axxon — relacion de jerarquia Master/Raw ─────────
        public const string MasterContactId     = "axx_mastercontactid";
        public const string MasterContactIdName = "axx_mastercontactidname";

        // ── Datos de persona ─────────────────────────────────────────
        public const string FirstName     = "firstname";
        public const string MiddleName    = "middlename";
        public const string LastName      = "lastname";
        public const string MobilePhone   = "mobilephone";
        public const string Description   = "description";
        public const string EmailAddress1 = "emailaddress1";
        public const string EmailAddress2 = "emailaddress2";

        // ── Dual Write / Dataverse for F&O ───────────────────────────
        public const string MsdynCompany             = "msdyn_company";
        public const string MsdynCompanyName         = "msdyn_companyname";
        public const string MsdynSellable            = "msdyn_sellable";
        public const string MsdynPartyId             = "msdyn_partyid";
        public const string MsdynPartyIdName         = "msdyn_partyidname";       // alias: msdyn_contactpersonid en el JSON
        public const string MsdynCustomerGroupId     = "msdyn_customergroupid";
        public const string MsdynCustomerGroupIdName = "msdyn_customergroupidname";
        public const string MsdynIdentificationNumber = "msdyn_identificationnumber";
        public const string MsdynIsProspect          = "msdyn_isprospect";
        public const string MsdynPartyCountry        = "msdyn_partycountry";
        public const string MsdynPartyStateProvince  = "msdyn_partystateprovince";
        public const string TransactionCurrencyId    = "transactioncurrencyid";
        public const string TransactionCurrencyIdName = "transactioncurrencyidname";
        public const string MsdynPaymentDay          = "msdyn_paymentday";
        public const string MsdynPaymentDayName      = "msdyn_paymentdayname";
        public const string MsdynPaymentSchedule     = "msdyn_paymentschedule";
        public const string MsdynPaymentScheduleName = "msdyn_paymentschedulename"; // alias: msdyn_customerpaymentmethod en el JSON
        public const string MsdynCustomerPaymentMethodName = "msdyn_customerpaymentmethodname";
        public const string MsdynSalesTaxGroup       = "msdyn_salestaxgroup";
        public const string MsdynSalesTaxGroupName   = "msdyn_salestaxgroupname";
        public const string MsdynPaymentTerms        = "msdyn_paymentterms";
        public const string MsdynPaymentTermsName    = "msdyn_paymenttermsname";
        public const string MsdynPrimaryContact      = "msdyn_primarycontact";
        public const string MsdynPrimaryContactName  = "msdyn_primarycontactname"; // alias: creditlimit en el JSON

        // ── Custom Annata/A365 ───────────────────────────────────────
        public const string A365CreditRating  = "a365_creditrating";
        public const string A365OnHoldStatus  = "a365_onholdstatus";
        public const string A365Notes         = "a365_notes";

        // ── Auditoria (solo lectura, no se propagan al Master) ───────
        public const string ModifiedBy     = "modifiedby";
        public const string ModifiedByName = "modifiedbyname";
        public const string ModifiedOn     = "modifiedon";

        // ── Campos que se propagan del Raw al Master ─────────────────
        // Excluye: contactid, axx_ismaster, axx_mastercontactid, modifiedby/on (audit)
        public static readonly IReadOnlyList<string> InheritedFields = new[]
        {
            FirstName, MiddleName, LastName, MobilePhone, Description,
            EmailAddress1, EmailAddress2, MsdynIsProspect,
            MsdynCompany, MsdynSellable, MsdynPartyId,
            MsdynCustomerGroupId, MsdynIdentificationNumber,
            MsdynPartyCountry, MsdynPartyStateProvince,
            TransactionCurrencyId, MsdynPaymentDay,
            MsdynPaymentSchedule, MsdynSalesTaxGroup,
            MsdynPaymentTerms, MsdynPrimaryContact,
            A365CreditRating, A365OnHoldStatus, A365Notes
        };

        /// <summary>Columnas que el plugin recupera del Contact para armar el mensaje.</summary>
        public static readonly string[] RequiredColumns = new[]
        {
            // Control
            IsMaster, MasterContactId, MasterContactIdName,
            // Persona
            FirstName, MiddleName, LastName, MobilePhone, Description,
            EmailAddress1, EmailAddress2, MsdynIsProspect,
            // F&O / Dual Write
            MsdynCompany, MsdynCompanyName,
            MsdynSellable,
            MsdynPartyId, MsdynPartyIdName,
            MsdynCustomerGroupId, MsdynCustomerGroupIdName,
            MsdynIdentificationNumber,
            MsdynPartyCountry, MsdynPartyStateProvince,
            TransactionCurrencyId, TransactionCurrencyIdName,
            MsdynPaymentDay, MsdynPaymentDayName,
            MsdynPaymentSchedule, MsdynPaymentScheduleName,
            MsdynCustomerPaymentMethodName,
            MsdynSalesTaxGroup, MsdynSalesTaxGroupName,
            MsdynPaymentTerms, MsdynPaymentTermsName,
            MsdynPrimaryContact, MsdynPrimaryContactName,
            // A365
            A365CreditRating, A365OnHoldStatus, A365Notes,
            // Auditoria
            ModifiedBy, ModifiedByName, ModifiedOn
        };
    }

    public static class PluginStages
    {
        public const int PreOperation  = 20;
        public const int PostOperation = 40;
    }

    public static class PluginMessages
    {
        public const string Create = "Create";
        public const string Update = "Update";
    }

    /// <summary>
    /// Logical names de las entidades relacionadas en los lookups.
    /// Necesarios para construir EntityReference al escribir en Dataverse desde la Function.
    /// VERIFICAR contra el metadata del environment antes de deploy.
    /// </summary>
    public static class RelatedEntities
    {
        public const string Account            = "account";           // msdyn_company
        public const string MsdynParty         = "msdyn_party";       // msdyn_partyid — VERIFICAR
        public const string MsdynCustomerGroup = "msdyn_customergroup"; // msdyn_customergroupid
        public const string TransactionCurrency = "transactioncurrency"; // transactioncurrencyid
        public const string MsdynPaymentDay    = "msdyn_paymentday";  // msdyn_paymentday — VERIFICAR si es Lookup u OptionSet
        public const string MsdynPaymentSchedule = "msdyn_paymentschedule"; // msdyn_paymentschedule
        public const string MsdynSalesTaxGroup = "msdyn_salestaxgroup"; // msdyn_salestaxgroup
        public const string MsdynPaymentTerms  = "msdyn_paymentterms"; // msdyn_paymentterms
        public const string Contact            = "contact";           // msdyn_primarycontact
        public const string SystemUser         = "systemuser";        // modifiedby
    }
}
