using System;
using System.Text;
using AxxonContacts.Plugins.Constants;
using AxxonContacts.Plugins.Models;
using AxxonContacts.Plugins.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Plugins
{
    /// <summary>
    /// Plugin thin: publica un ContactEventMessage (snapshot completo del Contact Raw)
    /// en Azure Service Bus. Sin logica de negocio.
    ///
    /// ================================================================
    /// REGISTRATION STEPS (Plugin Registration Tool)
    /// ================================================================
    ///
    ///   Step 1 — Create
    ///     Message:              Create
    ///     Entity:               contact
    ///     Stage:                Post-Operation (40)
    ///     Mode:                 Asynchronous
    ///     Filtering Attributes: (vacio)
    ///     Secure Configuration: {connectionString}|{queueName}
    ///
    ///   Step 2 — Update
    ///     Message:              Update
    ///     Entity:               contact
    ///     Stage:                Post-Operation (40)
    ///     Mode:                 Asynchronous
    ///     Filtering Attributes:
    ///       axx_ismaster, msdyn_identificationnumber,
    ///       firstname, middlename, lastname, mobilephone, description,
    ///       msdyn_company, msdyn_sellable, msdyn_partyid,
    ///       msdyn_customergroupid, msdyn_partycountry, msdyn_partystateprovince,
    ///       transactioncurrencyid, msdyn_paymentday, msdyn_paymentschedule,
    ///       msdyn_salestaxgroup, msdyn_paymentterms, msdyn_primarycontact,
    ///       a365_creditrating, a365_onholdstatus, a365_notes
    ///     Pre-Image (alias "PreImage"): todos los campos de RequiredColumns
    ///     Secure Configuration: {connectionString}|{queueName}
    ///
    /// CRITICO: NO incluir axx_mastercontactid en Filtering Attributes.
    /// ================================================================
    /// </summary>
    public class ContactEventPublisherPlugin : IPlugin
    {
        private readonly string _secureConfig;

        public ContactEventPublisherPlugin(string unsecureConfig, string secureConfig)
        {
            _secureConfig = secureConfig;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var context        = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing        = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service        = serviceFactory.CreateOrganizationService(null);

            try
            {
                tracing.Trace("[ContactEventPublisherPlugin] Inicio. Message={0}, Depth={1}",
                    context.MessageName, context.Depth);

                if (!IsContextValid(context, tracing)) return;

                if (context.Depth > 1)
                {
                    tracing.Trace("[ContactEventPublisherPlugin] Depth > 1. Anti-recursion. Saliendo.");
                    return;
                }

                var target = (Entity)context.InputParameters["Target"];

                // HydrateContact fusiona Target + PreImage para tener el estado completo.
                // Se usa SOLO para los guard checks (IsMaster, IdentificationNumber).
                var fullContact = HydrateContact(target, context, service, tracing);

                // Early exit: Masters no generan eventos
                if (fullContact.GetAttributeValue<bool>(ContactConstants.IsMaster))
                {
                    tracing.Trace("[ContactEventPublisherPlugin] Contact es Master. Skip.");
                    return;
                }

                var identificationNumber = fullContact.GetAttributeValue<string>(
                    ContactConstants.MsdynIdentificationNumber);

                if (string.IsNullOrWhiteSpace(identificationNumber))
                {
                    tracing.Trace("[ContactEventPublisherPlugin] msdyn_identificationnumber vacio. Skip.");
                    return;
                }

                // Para Update: el mensaje lleva SOLO los campos que cambiaron (Target delta)
                // mas los campos de identidad. Evita sobreescribir el master con datos
                // sin cambios de la PreImage que podrian pisar valores de otra fuente.
                // Para Create: se usa el contacto completo hidratado.
                var messageSource = context.MessageName == PluginMessages.Update
                    ? BuildUpdateDelta(target, fullContact)
                    : fullContact;

                var message = BuildMessage(messageSource, context.MessageName);
                var json    = SerializeToJson(message);

                var publisher = new ServiceBusPublisher(_secureConfig, tracing);
                publisher.PublishAsync(json, sessionId: identificationNumber)
                         .GetAwaiter()
                         .GetResult();

                tracing.Trace("[ContactEventPublisherPlugin] Mensaje publicado. SessionId={0}", identificationNumber);
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracing.Trace("[ContactEventPublisherPlugin] ERROR: {0}\n{1}", ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"ContactEventPublisherPlugin fallo: {ex.Message}", ex);
            }
        }

        // ────────────────────────────────────────────────────────────
        // BuildMessage — snapshot completo del Contact
        // ────────────────────────────────────────────────────────────

        private static ContactEventMessage BuildMessage(Entity c, string triggerMessage)
        {
            return new ContactEventMessage
            {
                // Identidad
                ContactId             = c.Id.ToString(),
                TriggerMessage        = triggerMessage,
                PublishedAt           = DateTimeOffset.UtcNow.ToString("O"),

                // Datos de persona
                FirstName             = c.GetAttributeValue<string>(ContactConstants.FirstName),
                MiddleName            = c.GetAttributeValue<string>(ContactConstants.MiddleName),
                LastName              = c.GetAttributeValue<string>(ContactConstants.LastName),
                MobilePhone           = c.GetAttributeValue<string>(ContactConstants.MobilePhone),
                Description           = c.GetAttributeValue<string>(ContactConstants.Description),
                EmailAddress1         = c.GetAttributeValue<string>(ContactConstants.EmailAddress1),
                EmailAddress2         = c.GetAttributeValue<string>(ContactConstants.EmailAddress2),
                MsdynIsProspect       = c.Contains(ContactConstants.MsdynIsProspect)
                                          ? (bool?)c.GetAttributeValue<bool>(ContactConstants.MsdynIsProspect)
                                          : null,

                // Control Master/Raw
                IsMaster              = c.GetAttributeValue<bool>(ContactConstants.IsMaster),
                MasterContactId       = GetRefId(c, ContactConstants.MasterContactId),
                MasterContactIdName   = c.GetAttributeValue<string>(ContactConstants.MasterContactIdName),

                // Dual Write / F&O
                MsdynCompany             = GetRefId(c, ContactConstants.MsdynCompany),
                MsdynCompanyName         = c.GetAttributeValue<string>(ContactConstants.MsdynCompanyName),
                MsdynSellable            = c.Contains(ContactConstants.MsdynSellable)
                                             ? (bool?)c.GetAttributeValue<bool>(ContactConstants.MsdynSellable)
                                             : null,
                MsdynPartyId             = GetRefId(c, ContactConstants.MsdynPartyId),
                MsdynContactPersonId     = c.GetAttributeValue<string>(ContactConstants.MsdynPartyIdName),
                MsdynCustomerGroupId     = GetRefId(c, ContactConstants.MsdynCustomerGroupId),
                MsdynCustomerGroupIdName = c.GetAttributeValue<string>(ContactConstants.MsdynCustomerGroupIdName),
                MsdynIdentificationNumber = c.GetAttributeValue<string>(ContactConstants.MsdynIdentificationNumber),
                MsdynPartyCountry        = c.GetAttributeValue<string>(ContactConstants.MsdynPartyCountry),
                MsdynPartyStateProvince  = c.GetAttributeValue<string>(ContactConstants.MsdynPartyStateProvince),
                TransactionCurrencyId    = GetRefId(c, ContactConstants.TransactionCurrencyId),
                TransactionCurrencyIdName = c.GetAttributeValue<string>(ContactConstants.TransactionCurrencyIdName),
                MsdynPaymentDay          = GetRefOrOptionSet(c, ContactConstants.MsdynPaymentDay),
                MsdynPaymentDayName      = c.GetAttributeValue<string>(ContactConstants.MsdynPaymentDayName),
                MsdynPaymentSchedule     = GetRefId(c, ContactConstants.MsdynPaymentSchedule),
                MsdynCustomerPaymentMethod = c.GetAttributeValue<string>(ContactConstants.MsdynPaymentScheduleName),
                MsdynCustomerPaymentMethodName = c.GetAttributeValue<string>(ContactConstants.MsdynCustomerPaymentMethodName),
                MsdynSalesTaxGroup       = GetRefId(c, ContactConstants.MsdynSalesTaxGroup),
                MsdynSalesTaxGroupName   = c.GetAttributeValue<string>(ContactConstants.MsdynSalesTaxGroupName),
                MsdynPaymentTerms        = GetRefId(c, ContactConstants.MsdynPaymentTerms),
                MsdynPaymentTermsName    = c.GetAttributeValue<string>(ContactConstants.MsdynPaymentTermsName),
                MsdynPrimaryContact      = GetRefId(c, ContactConstants.MsdynPrimaryContact),
                CreditLimit              = c.GetAttributeValue<string>(ContactConstants.MsdynPrimaryContactName),

                // A365
                A365CreditRating  = GetOptionSetValue(c, ContactConstants.A365CreditRating),
                A365OnHoldStatus  = c.Contains(ContactConstants.A365OnHoldStatus)
                                      ? (bool?)c.GetAttributeValue<bool>(ContactConstants.A365OnHoldStatus)
                                      : null,
                A365Notes         = c.GetAttributeValue<string>(ContactConstants.A365Notes),

                // Auditoria
                ModifiedBy     = GetRefId(c, ContactConstants.ModifiedBy),
                ModifiedByName = c.GetAttributeValue<string>(ContactConstants.ModifiedByName),
                ModifiedOn     = c.Contains(ContactConstants.ModifiedOn)
                                   ? c.GetAttributeValue<DateTime>(ContactConstants.ModifiedOn).ToString("O")
                                   : null
            };
        }

        // ── Helpers de extraccion de atributos ───────────────────────

        private static string GetRefId(Entity e, string field)
        {
            if (!e.Contains(field)) return null;
            var r = e.GetAttributeValue<EntityReference>(field);
            return r?.Id.ToString();
        }

        private static int? GetOptionSetValue(Entity e, string field)
        {
            if (!e.Contains(field)) return null;
            var osv = e.GetAttributeValue<OptionSetValue>(field);
            return osv?.Value;
        }

        /// <summary>
        /// msdyn_paymentday puede ser Lookup o OptionSet segun el environment.
        /// Intenta EntityReference primero; si el tipo real es OptionSet, el cast
        /// retornara null y se intenta como OptionSetValue.
        /// </summary>
        private static string GetRefOrOptionSet(Entity e, string field)
        {
            if (!e.Contains(field)) return null;
            var val = e[field];
            if (val is EntityReference er)   return er.Id.ToString();
            if (val is OptionSetValue osv)   return osv.Value.ToString();
            return val?.ToString();
        }

        // ────────────────────────────────────────────────────────────
        // JSON Serializer manual (sin NuGet — HttpClient + SAS en net462)
        // ────────────────────────────────────────────────────────────

        private static string SerializeToJson(ContactEventMessage m)
        {
            var sb = new StringBuilder(2048);
            sb.Append('{');
            AppendString(sb, "contactId",                m.ContactId);               sb.Append(',');
            AppendString(sb, "triggerMessage",           m.TriggerMessage);           sb.Append(',');
            AppendString(sb, "publishedAt",              m.PublishedAt);              sb.Append(',');
            AppendString(sb, "firstName",                m.FirstName);                sb.Append(',');
            AppendString(sb, "middleName",               m.MiddleName);               sb.Append(',');
            AppendString(sb, "lastName",                 m.LastName);                 sb.Append(',');
            AppendString(sb, "mobilePhone",              m.MobilePhone);              sb.Append(',');
            AppendString(sb, "description",              m.Description);              sb.Append(',');
            AppendString(sb, "emailAddress1",            m.EmailAddress1);            sb.Append(',');
            AppendString(sb, "emailAddress2",            m.EmailAddress2);            sb.Append(',');
            AppendNullableBool(sb, "msdynIsProspect",    m.MsdynIsProspect);          sb.Append(',');
            AppendBool  (sb, "isMaster",                 m.IsMaster);                 sb.Append(',');
            AppendString(sb, "masterContactId",          m.MasterContactId);          sb.Append(',');
            AppendString(sb, "masterContactIdName",      m.MasterContactIdName);      sb.Append(',');
            AppendString(sb, "msdynCompany",             m.MsdynCompany);             sb.Append(',');
            AppendString(sb, "msdynCompanyName",         m.MsdynCompanyName);         sb.Append(',');
            AppendNullableBool(sb, "msdynSellable",      m.MsdynSellable);            sb.Append(',');
            AppendString(sb, "msdynPartyId",             m.MsdynPartyId);             sb.Append(',');
            AppendString(sb, "msdynContactPersonId",     m.MsdynContactPersonId);     sb.Append(',');
            AppendString(sb, "msdynCustomerGroupId",     m.MsdynCustomerGroupId);     sb.Append(',');
            AppendString(sb, "msdynCustomerGroupIdName", m.MsdynCustomerGroupIdName); sb.Append(',');
            AppendString(sb, "msdynIdentificationNumber", m.MsdynIdentificationNumber); sb.Append(',');
            AppendString(sb, "msdynPartyCountry",        m.MsdynPartyCountry);        sb.Append(',');
            AppendString(sb, "msdynPartyStateProvince",  m.MsdynPartyStateProvince);  sb.Append(',');
            AppendString(sb, "transactionCurrencyId",    m.TransactionCurrencyId);    sb.Append(',');
            AppendString(sb, "transactionCurrencyIdName", m.TransactionCurrencyIdName); sb.Append(',');
            AppendString(sb, "msdynPaymentDay",          m.MsdynPaymentDay);          sb.Append(',');
            AppendString(sb, "msdynPaymentDayName",      m.MsdynPaymentDayName);      sb.Append(',');
            AppendString(sb, "msdynPaymentSchedule",     m.MsdynPaymentSchedule);     sb.Append(',');
            AppendString(sb, "msdynCustomerPaymentMethod", m.MsdynCustomerPaymentMethod); sb.Append(',');
            AppendString(sb, "msdynCustomerPaymentMethodName", m.MsdynCustomerPaymentMethodName); sb.Append(',');
            AppendString(sb, "msdynSalesTaxGroup",       m.MsdynSalesTaxGroup);       sb.Append(',');
            AppendString(sb, "msdynSalesTaxGroupName",   m.MsdynSalesTaxGroupName);   sb.Append(',');
            AppendString(sb, "msdynPaymentTerms",        m.MsdynPaymentTerms);        sb.Append(',');
            AppendString(sb, "msdynPaymentTermsName",    m.MsdynPaymentTermsName);    sb.Append(',');
            AppendString(sb, "msdynPrimaryContact",      m.MsdynPrimaryContact);      sb.Append(',');
            AppendString(sb, "creditLimit",              m.CreditLimit);              sb.Append(',');
            AppendNullableInt (sb, "a365CreditRating",   m.A365CreditRating);         sb.Append(',');
            AppendNullableBool(sb, "a365OnHoldStatus",   m.A365OnHoldStatus);         sb.Append(',');
            AppendString(sb, "a365Notes",                m.A365Notes);                sb.Append(',');
            AppendString(sb, "modifiedBy",               m.ModifiedBy);               sb.Append(',');
            AppendString(sb, "modifiedByName",           m.ModifiedByName);           sb.Append(',');
            AppendString(sb, "modifiedOn",               m.ModifiedOn);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":");
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"')
              .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                           .Replace("\n", "\\n").Replace("\r", "\\r"))
              .Append('"');
        }

        private static void AppendBool(StringBuilder sb, string key, bool value)
            => sb.Append('"').Append(key).Append("\":").Append(value ? "true" : "false");

        private static void AppendNullableBool(StringBuilder sb, string key, bool? value)
        {
            sb.Append('"').Append(key).Append("\":");
            if (value == null) sb.Append("null");
            else sb.Append(value.Value ? "true" : "false");
        }

        private static void AppendNullableInt(StringBuilder sb, string key, int? value)
        {
            sb.Append('"').Append(key).Append("\":");
            if (value == null) sb.Append("null");
            else sb.Append(value.Value);
        }

        // ────────────────────────────────────────────────────────────
        // BuildUpdateDelta
        // Construye una Entity con SOLO los campos del Target (los que cambiaron)
        // mas los campos de identidad tomados del fullContact hidratado.
        // Esto evita que la PreImage sobreescriba valores del master con datos
        // que no fueron parte del Update.
        // ────────────────────────────────────────────────────────────

        private static Entity BuildUpdateDelta(Entity target, Entity fullContact)
        {
            var delta = new Entity(target.LogicalName, target.Id);

            // Campos que realmente cambiaron en este Update
            foreach (var a in target.Attributes)
                delta[a.Key] = a.Value;

            // Campos de identidad: siempre necesarios aunque no hayan cambiado
            CopyIfMissing(delta, fullContact, ContactConstants.MsdynIdentificationNumber);
            CopyIfMissing(delta, fullContact, ContactConstants.IsMaster);
            CopyIfMissing(delta, fullContact, ContactConstants.MasterContactId);

            return delta;
        }

        private static void CopyIfMissing(Entity target, Entity source, string field)
        {
            if (!target.Contains(field) && source.Contains(field))
                target[field] = source[field];
        }

        // ────────────────────────────────────────────────────────────
        // Helpers de contexto / hidratacion
        // ────────────────────────────────────────────────────────────

        private static bool IsContextValid(IPluginExecutionContext context, ITracingService tracing)
        {
            if (!string.Equals(context.PrimaryEntityName, ContactConstants.EntityLogicalName,
                StringComparison.Ordinal))
            { tracing.Trace("[Plugin] Entidad incorrecta. Salir."); return false; }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity))
            { tracing.Trace("[Plugin] Target ausente. Salir."); return false; }

            if (context.Stage != PluginStages.PostOperation)
            { tracing.Trace("[Plugin] Stage incorrecto. Salir."); return false; }

            if (context.MessageName != PluginMessages.Create &&
                context.MessageName != PluginMessages.Update)
            { tracing.Trace("[Plugin] Message no soportado. Salir."); return false; }

            return true;
        }

        private static Entity HydrateContact(
            Entity target, IPluginExecutionContext context,
            IOrganizationService service, ITracingService tracing)
        {
            var hydrated = new Entity(target.LogicalName, target.Id);
            foreach (var a in target.Attributes) hydrated[a.Key] = a.Value;

            if (context.MessageName == PluginMessages.Update &&
                context.PreEntityImages.Contains("PreImage"))
            {
                foreach (var a in context.PreEntityImages["PreImage"].Attributes)
                    if (!hydrated.Contains(a.Key)) hydrated[a.Key] = a.Value;
            }

            // Retrieve defensivo si falta el numero de identificacion.
            // IsMaster null se trata como false — no necesita Retrieve.
            var needsRetrieve = !hydrated.Contains(ContactConstants.MsdynIdentificationNumber);

            if (needsRetrieve)
            {
                tracing.Trace("[Plugin] Retrieve fallback para contact {0}.", target.Id);
                var full = service.Retrieve(ContactConstants.EntityLogicalName, target.Id,
                    new ColumnSet(ContactConstants.RequiredColumns));
                foreach (var a in full.Attributes)
                    if (!hydrated.Contains(a.Key)) hydrated[a.Key] = a.Value;
            }

            return hydrated;
        }
    }
}
