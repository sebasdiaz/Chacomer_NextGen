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
    /// Plugin thin: publica un AccountEventMessage (snapshot del Account Raw) como
    /// payload del envelope EiP en Azure Service Bus. Sin logica de negocio.
    ///
    /// ================================================================
    /// REGISTRATION STEPS (Plugin Registration Tool)
    /// ================================================================
    ///   Step Create / Step Update
    ///     Message:              Create / Update
    ///     Entity:               account
    ///     Stage:                Post-Operation (40)
    ///     Mode:                 Asynchronous
    ///     Pre-Image (alias "preImage"): campos de RequiredColumns (solo Update)
    ///     Secure Configuration: {connectionString}|{queueName}
    ///       (queue de accounts, ej: account-master-matching)
    /// ================================================================
    /// </summary>
    public class AccountEventPublisherPlugin : IPlugin
    {
        private readonly string _secureConfig;

        public AccountEventPublisherPlugin(string unsecureConfig, string secureConfig)
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
                tracing.Trace("[AccountEventPublisherPlugin] Inicio. Message={0}, Depth={1}",
                    context.MessageName, context.Depth);

                if (!IsContextValid(context, tracing)) return;

                if (context.Depth > 1)
                {
                    tracing.Trace("[AccountEventPublisherPlugin] Depth > 1. Anti-recursion. Saliendo.");
                    return;
                }

                var target = (Entity)context.InputParameters["Target"];
                var fullAccount = HydrateAccount(target, context, service, tracing);

                if (fullAccount.GetAttributeValue<bool>(AccountConstants.IsMaster))
                {
                    tracing.Trace("[AccountEventPublisherPlugin] Account es Master. Skip.");
                    return;
                }

                var identificationNumber = fullAccount.GetAttributeValue<string>(
                    AccountConstants.MsdynIdentificationNumber);

                if (string.IsNullOrWhiteSpace(identificationNumber))
                {
                    tracing.Trace("[AccountEventPublisherPlugin] msdyn_identificationnumber vacio. Skip.");
                    return;
                }

                var identificationChanged = target.Contains(AccountConstants.MsdynIdentificationNumber);

                var messageSource = context.MessageName == PluginMessages.Update
                    ? BuildUpdateDelta(target, fullAccount)
                    : fullAccount;

                var message = BuildMessage(messageSource, context.MessageName);
                message.IdentificationNumberChanged = identificationChanged;

                var payloadJson = SerializeToJson(message);
                var envelope = EipEnvelope.Wrap(
                    payloadJson,
                    entityType:   "account",
                    operation:    context.MessageName.ToLowerInvariant(),
                    partitionKey: identificationNumber,
                    correlationId: context.CorrelationId);

                var publisher = new ServiceBusPublisher(_secureConfig, tracing);
                publisher.PublishAsync(envelope, sessionId: identificationNumber)
                         .GetAwaiter()
                         .GetResult();

                tracing.Trace("[AccountEventPublisherPlugin] Envelope publicado. SessionId={0}", identificationNumber);
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracing.Trace("[AccountEventPublisherPlugin] ERROR: {0}\n{1}", ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"AccountEventPublisherPlugin fallo: {ex.Message}", ex);
            }
        }

        // ────────────────────────────────────────────────────────────
        // BuildMessage
        // ────────────────────────────────────────────────────────────

        private static AccountEventMessage BuildMessage(Entity a, string triggerMessage)
        {
            return new AccountEventMessage
            {
                AccountId      = a.Id.ToString(),
                TriggerMessage = triggerMessage,
                PublishedAt    = DateTimeOffset.UtcNow.ToString("O"),

                Name          = a.GetAttributeValue<string>(AccountConstants.Name),
                Telephone1    = a.GetAttributeValue<string>(AccountConstants.Telephone1),
                EmailAddress1 = a.GetAttributeValue<string>(AccountConstants.EmailAddress1),
                Description   = a.GetAttributeValue<string>(AccountConstants.Description),

                IsMaster        = a.GetAttributeValue<bool>(AccountConstants.IsMaster),
                MasterAccountId = GetRefId(a, AccountConstants.MasterAccountId),

                MsdynCompany              = GetRefId(a, AccountConstants.MsdynCompany),
                MsdynIdentificationNumber = a.GetAttributeValue<string>(AccountConstants.MsdynIdentificationNumber),

                ModifiedBy = GetRefId(a, AccountConstants.ModifiedBy),
                ModifiedOn = a.Contains(AccountConstants.ModifiedOn)
                               ? a.GetAttributeValue<DateTime>(AccountConstants.ModifiedOn).ToString("O")
                               : null
            };
        }

        // ── JSON manual (nombres alineados con la Function) ──────────

        private static string SerializeToJson(AccountEventMessage m)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');
            AppendString(sb, "accountId",                   m.AccountId);                    sb.Append(',');
            AppendString(sb, "triggerMessage",              m.TriggerMessage);               sb.Append(',');
            AppendString(sb, "publishedAt",                 m.PublishedAt);                  sb.Append(',');
            AppendBool  (sb, "identificationNumberChanged", m.IdentificationNumberChanged);  sb.Append(',');
            AppendString(sb, "name",                        m.Name);                         sb.Append(',');
            AppendString(sb, "telephone1",                  m.Telephone1);                   sb.Append(',');
            AppendString(sb, "emailAddress1",               m.EmailAddress1);                sb.Append(',');
            AppendString(sb, "description",                 m.Description);                  sb.Append(',');
            AppendBool  (sb, "isMaster",                    m.IsMaster);                     sb.Append(',');
            AppendString(sb, "masterAccountId",             m.MasterAccountId);              sb.Append(',');
            AppendString(sb, "msdynCompany",                m.MsdynCompany);                 sb.Append(',');
            AppendString(sb, "msdynIdentificationNumber",   m.MsdynIdentificationNumber);    sb.Append(',');
            AppendString(sb, "modifiedBy",                  m.ModifiedBy);                   sb.Append(',');
            AppendString(sb, "modifiedOn",                  m.ModifiedOn);
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

        // ── Delta / hidratacion (mismo patron que ContactEventPublisherPlugin) ──

        private static Entity BuildUpdateDelta(Entity target, Entity fullAccount)
        {
            var delta = new Entity(target.LogicalName, target.Id);
            foreach (var att in target.Attributes)
                delta[att.Key] = att.Value;

            CopyIfMissing(delta, fullAccount, AccountConstants.MsdynIdentificationNumber);
            CopyIfMissing(delta, fullAccount, AccountConstants.IsMaster);
            CopyIfMissing(delta, fullAccount, AccountConstants.MasterAccountId);

            return delta;
        }

        private static void CopyIfMissing(Entity target, Entity source, string field)
        {
            if (!target.Contains(field) && source.Contains(field))
                target[field] = source[field];
        }

        private static string GetRefId(Entity e, string field)
        {
            if (!e.Contains(field)) return null;
            var r = e.GetAttributeValue<EntityReference>(field);
            return r?.Id.ToString();
        }

        private static bool IsContextValid(IPluginExecutionContext context, ITracingService tracing)
        {
            if (!string.Equals(context.PrimaryEntityName, AccountConstants.EntityLogicalName,
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

        private static Entity HydrateAccount(
            Entity target, IPluginExecutionContext context,
            IOrganizationService service, ITracingService tracing)
        {
            var hydrated = new Entity(target.LogicalName, target.Id);
            foreach (var att in target.Attributes) hydrated[att.Key] = att.Value;

            if (context.MessageName == PluginMessages.Update &&
                context.PreEntityImages.Contains("preImage"))
            {
                foreach (var att in context.PreEntityImages["preImage"].Attributes)
                    if (!hydrated.Contains(att.Key)) hydrated[att.Key] = att.Value;
            }

            var needsRetrieve = !hydrated.Contains(AccountConstants.MsdynIdentificationNumber);
            if (needsRetrieve)
            {
                tracing.Trace("[Plugin] Retrieve fallback para account {0}.", target.Id);
                var full = service.Retrieve(AccountConstants.EntityLogicalName, target.Id,
                    new ColumnSet(AccountConstants.RequiredColumns));
                foreach (var att in full.Attributes)
                    if (!hydrated.Contains(att.Key)) hydrated[att.Key] = att.Value;
            }

            return hydrated;
        }
    }
}
