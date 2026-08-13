using System;
using AxxonContacts.Plugins.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Plugins
{
    /// <summary>
    /// Garantiza que un registro master (axx_ismaster = true) nunca llegue a F&amp;O.
    ///
    /// Hoy eso se cumple por convencion: MasterMatchingService crea el contact master
    /// sin msdyn_company y con msdyn_sellable = false, y AccountMasterMatchingService
    /// crea el account master con customertypecode = 12. Nada impide que un usuario,
    /// un flow o un camino de codigo nuevo pisen esos valores: si eso pasa, Dual Write
    /// empuja el master al ERP y aparece un cliente duplicado.
    ///
    /// Este plugin convierte la convencion en garantia: corre en Pre-Operation y
    /// corrige el Target antes de que el resto del pipeline —los steps de Dual Write
    /// incluidos— lo vea. No bloquea la operacion: la fuerza. Un error de tipeo en el
    /// form no tiene que abortar un alta, pero tampoco puede terminar en el ERP.
    ///
    /// ================================================================
    /// REGISTRATION STEPS (Plugin Registration Tool)
    /// ================================================================
    /// Los cuatro steps: Stage = Pre-Operation (20), Mode = Synchronous, Rank = 1
    /// (lo mas temprano posible, antes de cualquier step de Dual Write).
    ///
    ///   Step 1 — contact / Create
    ///     Filtering Attributes: (no aplica)
    ///     Pre-Image:            (no aplica)
    ///
    ///   Step 2 — contact / Update
    ///     Filtering Attributes: axx_ismaster, msdyn_company, msdyn_sellable
    ///     Pre-Image (alias "preImage"): axx_ismaster
    ///
    ///   Step 3 — account / Create
    ///     Filtering Attributes: (no aplica)
    ///     Pre-Image:            (no aplica)
    ///
    ///   Step 4 — account / Update
    ///     Filtering Attributes: axx_ismaster, customertypecode
    ///     Pre-Image (alias "preImage"): axx_ismaster
    ///
    /// Sin la Pre-Image el plugin igual funciona: cae a un Retrieve de axx_ismaster.
    /// Los Filtering Attributes son los que evitan que ese Retrieve se pague en cada
    /// update de contact o account.
    /// ================================================================
    /// </summary>
    public class MasterFoIsolationPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var context        = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing        = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            try
            {
                if (!IsContextValid(context, tracing)) return;

                var entityName = context.PrimaryEntityName;
                var target     = (Entity)context.InputParameters["Target"];

                // El Target que apaga el flag saca al registro de master: deja de aplicar.
                var targetMaster = target.Contains(MasterIsolationConstants.IsMaster)
                    ? (bool?)target.GetAttributeValue<bool>(MasterIsolationConstants.IsMaster)
                    : null;

                if (targetMaster == false)
                {
                    tracing.Trace("[MasterFoIsolationPlugin] El Target apaga axx_ismaster. No aplica.");
                    return;
                }

                // becomesMaster: el registro nace master o pasa a serlo en esta operacion.
                // Ahi no alcanza con revisar el Target — hay que pisar tambien lo que ya
                // esta guardado, que puede venir de cuando el registro era un raw.
                var becomesMaster = targetMaster == true;

                if (!becomesMaster && !TouchesGuardedField(target, entityName))
                {
                    tracing.Trace("[MasterFoIsolationPlugin] El Target no toca campos de aislamiento. Nada que hacer.");
                    return;
                }

                if (!becomesMaster &&
                    !IsStoredMaster(context, serviceFactory, entityName, target.Id, tracing))
                {
                    tracing.Trace("[MasterFoIsolationPlugin] {0} {1} no es master. Nada que hacer.",
                        entityName, target.Id);
                    return;
                }

                if (entityName == ContactConstants.EntityLogicalName)
                    EnforceContact(target, becomesMaster, tracing);
                else
                    EnforceAccount(target, becomesMaster, tracing);
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracing.Trace("[MasterFoIsolationPlugin] ERROR: {0}\n{1}", ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"MasterFoIsolationPlugin fallo: {ex.Message}", ex);
            }
        }

        // ────────────────────────────────────────────────────────────
        // Reglas por entidad
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sin msdyn_company el mapa de Dual Write no tiene legal entity a donde rutear
        /// el contacto, y msdyn_sellable = false lo deja afuera del mapa de clientes.
        /// </summary>
        private static void EnforceContact(Entity target, bool forceStored, ITracingService tracing)
        {
            Enforce(target, ContactConstants.MsdynCompany,  null,  forceStored, tracing);
            Enforce(target, ContactConstants.MsdynSellable, false, forceStored, tracing);
        }

        /// <summary>
        /// El account master si lleva msdyn_company (lo exige el plugin de Dual Write),
        /// asi que lo unico que lo mantiene fuera del ERP es el customertypecode.
        /// </summary>
        private static void EnforceAccount(Entity target, bool forceStored, ITracingService tracing)
        {
            Enforce(target,
                MasterIsolationConstants.CustomerTypeCode,
                new OptionSetValue(MasterIsolationConstants.MasterCustomerTypeCode),
                forceStored,
                tracing);
        }

        private static bool TouchesGuardedField(Entity target, string entityName)
        {
            if (entityName == ContactConstants.EntityLogicalName)
                return target.Contains(ContactConstants.MsdynCompany)
                    || target.Contains(ContactConstants.MsdynSellable);

            return target.Contains(MasterIsolationConstants.CustomerTypeCode);
        }

        /// <summary>
        /// Escribe el valor requerido en el Target solo cuando hace falta: si el Target
        /// trae otro valor, o si el registro recien pasa a master y hay que pisar el
        /// valor guardado. Escribir de mas ensuciaria el registro en cada update.
        /// </summary>
        private static void Enforce(
            Entity target, string field, object required, bool forceStored, ITracingService tracing)
        {
            var inTarget = target.Contains(field);

            if (inTarget && AreEqual(target[field], required)) return;
            if (!inTarget && !forceStored) return;

            target[field] = required;
            tracing.Trace("[MasterFoIsolationPlugin] {0} forzado a {1} (registro master).",
                field, Describe(required));
        }

        private static bool AreEqual(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is OptionSetValue oa && b is OptionSetValue ob)     return oa.Value == ob.Value;
            if (a is EntityReference ra && b is EntityReference rb)   return ra.Id == rb.Id;
            return a.Equals(b);
        }

        private static string Describe(object value)
        {
            if (value == null) return "null";
            if (value is OptionSetValue osv) return osv.Value.ToString();
            return value.ToString();
        }

        // ────────────────────────────────────────────────────────────
        // Helpers de contexto
        // ────────────────────────────────────────────────────────────

        private static bool IsContextValid(IPluginExecutionContext context, ITracingService tracing)
        {
            if (context.Stage != PluginStages.PreOperation)
            { tracing.Trace("[MasterFoIsolationPlugin] Stage incorrecto. Salir."); return false; }

            if (context.MessageName != PluginMessages.Create &&
                context.MessageName != PluginMessages.Update)
            { tracing.Trace("[MasterFoIsolationPlugin] Message no soportado. Salir."); return false; }

            if (context.PrimaryEntityName != ContactConstants.EntityLogicalName &&
                context.PrimaryEntityName != MasterIsolationConstants.AccountEntityLogicalName)
            { tracing.Trace("[MasterFoIsolationPlugin] Entidad incorrecta. Salir."); return false; }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity))
            { tracing.Trace("[MasterFoIsolationPlugin] Target ausente. Salir."); return false; }

            return true;
        }

        private static bool IsStoredMaster(
            IPluginExecutionContext context, IOrganizationServiceFactory serviceFactory,
            string entityName, Guid recordId, ITracingService tracing)
        {
            if (context.PreEntityImages.Contains(MasterIsolationConstants.PreImageAlias))
            {
                var preImage = context.PreEntityImages[MasterIsolationConstants.PreImageAlias];
                if (preImage.Contains(MasterIsolationConstants.IsMaster))
                    return preImage.GetAttributeValue<bool>(MasterIsolationConstants.IsMaster);
            }

            tracing.Trace("[MasterFoIsolationPlugin] PreImage sin axx_ismaster. Retrieve de {0} {1}.",
                entityName, recordId);

            var service = serviceFactory.CreateOrganizationService(null);
            var stored  = service.Retrieve(entityName, recordId,
                new ColumnSet(MasterIsolationConstants.IsMaster));

            return stored.GetAttributeValue<bool>(MasterIsolationConstants.IsMaster);
        }
    }
}
