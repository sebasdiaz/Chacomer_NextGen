using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Orquesta la sincronizacion contact (Dataverse) -> CustomersV3 (F&O):
    ///   1. Recupera el contact con los campos del mapeo CustomersV3_Contact.json.
    ///   2. Idempotencia: si msdyn_contactpersonid ya tiene valor, el contact ya
    ///      fue sincronizado y se omite el insert.
    ///   3. Resuelve dataAreaId desde msdyn_company.cdm_companycode (sin compania -> DLQ).
    ///   4. Resuelve los campos de los lookups relacionados (party number, grupo,
    ///      moneda, terminos de pago, etc.) con Retrieves individuales.
    ///   5. Inserta en F&O y escribe el CustomerAccount generado de vuelta en
    ///      msdyn_contactpersonid del contact.
    /// </summary>
    public class ContactCustomerSyncService : IContactCustomerSyncService
    {
        private const string ContactEntity = "contact";

        // Campos propios del contact segun el mapeo
        private static readonly string[] ContactColumns =
        {
            "msdyn_contactpersonid",
            "msdyn_identificationnumber",
            "msdyn_partycountry",
            "msdyn_partystateprovince",
            "description",
            "creditlimit",
            "a365_creditrating",
            "a365_onholdstatus",
            "a365_notes",
            "msdyn_sellable",
            "msdyn_partyid",
            "msdyn_customergroupid",
            "transactioncurrencyid",
            "msdyn_paymentday",
            "msdyn_paymentschedule",
            "msdyn_customerpaymentmethod",
            "msdyn_salestaxgroup",
            "msdyn_paymentterms",
            "msdyn_primarycontact",
            "msdyn_company"
        };

        // a365_onholdstatus (OptionSet CRM) -> OnHoldStatus (enum CustVendorBlocked F&O).
        // Inverso del valueMap de CustomersV3_Contact.json, con los nombres de
        // miembro del enum OData de F&O (case exacto requerido por la API).
        private static readonly Dictionary<int, string> OnHoldStatusMap = new()
        {
            [806380000] = "No",
            [806380001] = "Invoice",
            [806380002] = "All",
            [806380003] = "Payment",
            [806380004] = "Requisition",
            [806380005] = "Never"
        };

        private readonly IOrganizationService _orgService;
        private readonly IFoCustomerService _foCustomerService;
        private readonly ILogger<ContactCustomerSyncService> _logger;

        public ContactCustomerSyncService(
            IOrganizationService orgService,
            IFoCustomerService foCustomerService,
            ILogger<ContactCustomerSyncService> logger)
        {
            _orgService        = orgService        ?? throw new ArgumentNullException(nameof(orgService));
            _foCustomerService = foCustomerService ?? throw new ArgumentNullException(nameof(foCustomerService));
            _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAsync(Guid contactId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[ContactCustomerSyncService] Inicio. ContactId={ContactId}", contactId);

            var contact = RetrieveContact(contactId);

            // Idempotencia: msdyn_contactpersonid se setea en el write-back final.
            // Si ya tiene valor, este contact ya existe como customer en F&O.
            var existingAccount = contact.GetAttributeValue<string>("msdyn_contactpersonid");
            if (!string.IsNullOrWhiteSpace(existingAccount))
            {
                _logger.LogInformation(
                    "[ContactCustomerSyncService] Contact {ContactId} ya sincronizado " +
                    "(msdyn_contactpersonid={CustomerAccount}). Se omite el insert.",
                    contactId, existingAccount);
                return;
            }

            var dataAreaId = ResolveDataAreaId(contact);
            var payload    = BuildFoCustomer(contact, dataAreaId);

            var created = await _foCustomerService.CreateCustomerAsync(payload, cancellationToken);

            WriteBackCustomerAccount(contactId, created.CustomerAccount);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Fin. ContactId={ContactId} | CustomerAccount={CustomerAccount}",
                contactId, created.CustomerAccount ?? "N/A");
        }

        // ── Lectura de Dataverse ──────────────────────────────────────

        private Entity RetrieveContact(Guid contactId)
        {
            try
            {
                return _orgService.Retrieve(
                    ContactEntity,
                    contactId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet(ContactColumns));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                when (ex.Detail.ErrorCode == unchecked((int)0x80040217)) // ObjectDoesNotExist
            {
                throw new NonRetryableSyncException(
                    $"El contact {contactId} no existe en Dataverse.");
            }
        }

        private string ResolveDataAreaId(Entity contact)
        {
            var companyRef = contact.GetAttributeValue<EntityReference>("msdyn_company");
            if (companyRef == null)
                throw new NonRetryableSyncException(
                    $"El contact {contact.Id} no tiene compania (msdyn_company). " +
                    "No se puede determinar el dataAreaId para el insert en F&O.");

            var dataAreaId = LookupField(companyRef, "cdm_companycode");
            if (string.IsNullOrWhiteSpace(dataAreaId))
                throw new NonRetryableSyncException(
                    $"La compania {companyRef.Id} del contact {contact.Id} no tiene cdm_companycode.");

            return dataAreaId;
        }

        // ── Mapeo CRM -> F&O (CustomersV3_Contact.json invertido) ────

        private FoCustomerV3 BuildFoCustomer(Entity contact, string dataAreaId)
        {
            var payload = new FoCustomerV3
            {
                DataAreaId = dataAreaId,
                PartyType  = "Person",

                IdentificationNumber = contact.GetAttributeValue<string>("msdyn_identificationnumber"),
                PartyCountry         = contact.GetAttributeValue<string>("msdyn_partycountry"),
                PartyState           = contact.GetAttributeValue<string>("msdyn_partystateprovince"),
                SalesMemo            = contact.GetAttributeValue<string>("description"),
                CredManNotes         = contact.GetAttributeValue<string>("a365_notes"),
                CreditLimit          = contact.GetAttributeValue<Money>("creditlimit")?.Value,

                PartyNumber     = LookupField(contact, "msdyn_partyid",             "msdyn_partynumber"),
                CustomerGroupId = LookupField(contact, "msdyn_customergroupid",     "msdyn_groupid"),
                SalesCurrencyCode = LookupField(contact, "transactioncurrencyid",   "isocurrencycode"),
                PaymentDay      = LookupField(contact, "msdyn_paymentday",          "msdyn_name"),
                PaymentSchedule = LookupField(contact, "msdyn_paymentschedule",     "msdyn_name"),
                PaymentMethod   = LookupField(contact, "msdyn_customerpaymentmethod", "msdyn_name"),
                SalesTaxGroup   = LookupField(contact, "msdyn_salestaxgroup",       "msdyn_name"),
                PaymentTerms    = LookupField(contact, "msdyn_paymentterms",        "msdyn_name"),
                ContactPersonId = LookupField(contact, "msdyn_primarycontact",      "msdyn_contactforpartynumber"),

                CreditRating = ResolveCreditRating(contact),
                OnHoldStatus = ResolveOnHoldStatus(contact),
                A365Sellable = ResolveSellable(contact)
            };

            // CustomerAccount se omite: F&O lo genera por number sequence.
            return payload;
        }

        private string? ResolveCreditRating(Entity contact)
        {
            // a365_creditrating puede ser texto u OptionSet segun el environment;
            // para OptionSet se usa la etiqueta formateada.
            if (!contact.Attributes.TryGetValue("a365_creditrating", out var value))
                return null;

            return value switch
            {
                string s         => s,
                OptionSetValue _ => contact.FormattedValues.TryGetValue("a365_creditrating", out var label)
                                        ? label
                                        : null,
                _                => null
            };
        }

        private string? ResolveOnHoldStatus(Entity contact)
        {
            var osv = contact.GetAttributeValue<OptionSetValue>("a365_onholdstatus");
            if (osv == null)
                return null;

            if (OnHoldStatusMap.TryGetValue(osv.Value, out var foValue))
                return foValue;

            _logger.LogWarning(
                "[ContactCustomerSyncService] a365_onholdstatus={Value} sin mapeo a F&O. " +
                "Se omite OnHoldStatus.", osv.Value);
            return null;
        }

        private static string? ResolveSellable(Entity contact)
        {
            var sellable = contact.GetAttributeValue<bool?>("msdyn_sellable");
            return sellable.HasValue ? (sellable.Value ? "Yes" : "No") : null;
        }

        // ── Resolucion de lookups ─────────────────────────────────────

        private string? LookupField(Entity contact, string attribute, string relatedField)
        {
            var reference = contact.GetAttributeValue<EntityReference>(attribute);
            return reference == null ? null : LookupField(reference, relatedField);
        }

        private string? LookupField(EntityReference reference, string relatedField)
        {
            var related = _orgService.Retrieve(
                reference.LogicalName,
                reference.Id,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(relatedField));

            return related.GetAttributeValue<string>(relatedField);
        }

        // ── Write-back ────────────────────────────────────────────────

        private void WriteBackCustomerAccount(Guid contactId, string? customerAccount)
        {
            if (string.IsNullOrWhiteSpace(customerAccount))
            {
                _logger.LogWarning(
                    "[ContactCustomerSyncService] F&O no devolvio CustomerAccount para el " +
                    "contact {ContactId}. Se omite el write-back (sin idempotencia para reintentos).",
                    contactId);
                return;
            }

            var update = new Entity(ContactEntity, contactId)
            {
                ["msdyn_contactpersonid"] = customerAccount
            };

            _orgService.Update(update);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Write-back OK. Contact={ContactId} | " +
                "msdyn_contactpersonid={CustomerAccount}",
                contactId, customerAccount);
        }
    }
}
