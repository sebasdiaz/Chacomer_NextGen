using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System.ServiceModel;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Services
{
    public class MasterMatchingService
    {
        private const string EntityLogicalName    = "contact";
        private const string IsMaster             = "axx_ismaster";
        private const string MasterContactId      = "axx_mastercontactid";
        private const string IdentificationNumber = "msdyn_identificationnumber";
        private const int    BulkBatchSize        = 1000;

        private readonly IOrganizationService _service;
        private readonly ILogger _logger;

        public MasterMatchingService(IOrganizationService service, ILogger logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Solo procesa eventos Create de contactos raw (no master).
        /// Si ya existe un master para el msdyn_identificationnumber → retorna su referencia (sin crear uno nuevo).
        /// Si no existe → crea el master, linkea todos los raws y retorna la referencia del nuevo master.
        /// Retorna null si el evento se ignora (trigger distinto a Create, es master, o sin identification).
        /// </summary>
        public async Task<EntityReference?> ProcessAsync(ContactEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[MasterMatchingService] Procesando Contact {ContactId} | Identification={Identification} | Trigger={Trigger}",
                message.ContactId, message.MsdynIdentificationNumber, message.TriggerMessage);

            // Procesar Create, o Update cuando msdyn_identificationnumber fue establecido en esta operacion.
            // En Dual Write el contacto se crea primero sin RUC y se actualiza despues con el campo,
            // por lo que el evento relevante para crear el master puede ser un Update.
            bool isCreate = string.Equals(message.TriggerMessage, "Create", StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(message.TriggerMessage, "Update", StringComparison.OrdinalIgnoreCase);

            if (!isCreate && !isUpdate)
            {
                _logger.LogInformation(
                    "[MasterMatchingService] Evento '{Trigger}' ignorado (trigger no relevante).",
                    message.TriggerMessage);
                return null;
            }

            // El contacto mismo no debe ser master
            if (message.IsMaster == true)
            {
                _logger.LogInformation("[MasterMatchingService] Contact es Master. Skip.");
                return null;
            }

            // Si identification no llego en el payload (Step sin PreImage completo),
            // se busca directamente en Dataverse.
            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogInformation(
                    "[MasterMatchingService] IdentificationNumber ausente en payload. Recuperando Contact {ContactId} de Dataverse.",
                    message.ContactId);
                message = await EnrichFromDataverseAsync(message);
            }

            // Identificacion requerida
            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogWarning("[MasterMatchingService] IdentificationNumber vacio tras enrich. Skip.");
                return null;
            }

            // Si ya existe un master → linkear los raws que aún no estén asociados y retornar su referencia
            var existingMaster = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber);
            if (existingMaster != null)
            {
                _logger.LogInformation(
                    "[MasterMatchingService] Master {MasterId} ya existe para '{Identification}'. Linkeando raws pendientes.",
                    existingMaster.Id, message.MsdynIdentificationNumber);
                await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, existingMaster.ToEntityReference());
                return existingMaster.ToEntityReference();
            }

            // No existe master → crear y linkear todos los raws
            _logger.LogInformation(
                "[MasterMatchingService] Sin Master para '{Identification}'. Creando.",
                message.MsdynIdentificationNumber);

            var newMasterRef = await CreateMasterAsync(message);
            await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, newMasterRef);

            _logger.LogInformation(
                "[MasterMatchingService] Completado. Contact={ContactId} | Master={MasterId}",
                message.ContactId, newMasterRef.Id);

            return newMasterRef;
        }

        // ────────────────────────────────────────────────────────────
        // EnrichFromDataverse
        // ────────────────────────────────────────────────────────────

        private async Task<ContactEventMessage> EnrichFromDataverseAsync(ContactEventMessage message)
        {
            try
            {
                var record = await Task.Run(() =>
                    _service.Retrieve(EntityLogicalName, message.ContactId,
                        new ColumnSet(IsMaster, MasterContactId, IdentificationNumber,
                            "firstname", "lastname", "mobilephone", "emailaddress1")));

                if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
                    message.MsdynIdentificationNumber = record.GetAttributeValue<string>(IdentificationNumber);
                message.IsMaster = record.GetAttributeValue<bool>(IsMaster);
                message.MasterContactId = record.GetAttributeValue<EntityReference>(MasterContactId)?.Id ?? message.MasterContactId;
                if (string.IsNullOrWhiteSpace(message.FirstName))
                    message.FirstName = record.GetAttributeValue<string>("firstname");
                if (string.IsNullOrWhiteSpace(message.LastName))
                    message.LastName = record.GetAttributeValue<string>("lastname");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[MasterMatchingService] No se pudo recuperar Contact {ContactId} de Dataverse. Usando payload original.",
                    message.ContactId);
            }
            return message;
        }

        // ────────────────────────────────────────────────────────────
        // RetrieveCurrentState
        // ────────────────────────────────────────────────────────────

        private async Task<Entity?> RetrieveCurrentStateAsync(Guid contactId)
        {
            try
            {
                return await Task.Run(() =>
                    _service.Retrieve(EntityLogicalName, contactId,
                        new ColumnSet(IsMaster, MasterContactId)));
            }
            catch (FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> ex)
                when (ex.Detail?.ErrorCode == unchecked((int)0x80040217))
            {
                return null; // ObjectDoesNotExist — contacto eliminado
            }
        }

        // ────────────────────────────────────────────────────────────
        // FindMasterByIdentification
        // ────────────────────────────────────────────────────────────

        private async Task<Entity?> FindMasterByIdentificationAsync(string identificationNumber)
        {
            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet(IsMaster, IdentificationNumber),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(IdentificationNumber, ConditionOperator.Equal, identificationNumber),
                        new ConditionExpression(IsMaster, ConditionOperator.Equal, true)
                    }
                },
                TopCount = 2
            };

            var results = await Task.Run(() => _service.RetrieveMultiple(query));

            if (results.Entities.Count > 1)
                _logger.LogWarning(
                    "[MasterMatchingService] {Count} Masters para '{Identification}'. Usando el primero ({Id}).",
                    results.Entities.Count, identificationNumber, results.Entities[0].Id);

            return results.Entities.Count > 0 ? results.Entities[0] : null;
        }

        // ────────────────────────────────────────────────────────────
        // CreateMaster
        // ────────────────────────────────────────────────────────────

        private async Task<EntityReference> CreateMasterAsync(ContactEventMessage message)
        {
            var master = BuildMasterEntity(message);

            try
            {
                var masterId = await Task.Run(() => _service.Create(master));
                _logger.LogInformation("[MasterMatchingService] Master creado. Id={Id}", masterId);
                return new EntityReference(EntityLogicalName, masterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[MasterMatchingService] Create fallo: {Error}. Re-buscando (posible race condition).",
                    ex.Message);

                var existing = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber!);
                if (existing != null)
                {
                    _logger.LogInformation("[MasterMatchingService] Race condition resuelta. Master={Id}", existing.Id);
                    return existing.ToEntityReference();
                }
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────
        // BuildMasterEntity
        // Construye la entidad del master a partir del mensaje Create.
        // msdyn_company y msdyn_sellable siempre quedan en null/false en el master.
        // ────────────────────────────────────────────────────────────

        private static Entity BuildMasterEntity(ContactEventMessage m)
        {
            var e = new Entity(EntityLogicalName);

            e[IsMaster]              = true;
            e["msdyn_sellable"]      = false;
            e["a365_contacttype"]    = new OptionSetValue(727000001);

            // Datos de persona
            SetString(e, "firstname",     m.FirstName);
            SetString(e, "middlename",    m.MiddleName);
            SetString(e, "lastname",      m.LastName);
            SetString(e, "mobilephone",   m.MobilePhone);
            SetString(e, "description",   m.Description);
            SetString(e, "emailaddress1", m.EmailAddress1);
            SetString(e, "emailaddress2", m.EmailAddress2);
            if (m.MsdynIsProspect.HasValue) e["msdyn_isprospect"] = m.MsdynIsProspect.Value;

            // Dual Write / F&O — Lookups
            // msdyn_company NO se copia al master (siempre null)
            // msdyn_partyid NO se copia: la clave unica (partyid + company=null) ya la tiene el raw;
            // copiarla al master violaría la constraint "Party Key With No Company".
            SetRef(e, "msdyn_customergroupid", "msdyn_customergroup", m.MsdynCustomerGroupId);
            SetRef(e, "transactioncurrencyid", "transactioncurrency", m.TransactionCurrencyId);
            SetRef(e, "msdyn_paymentschedule", "msdyn_paymentschedule", m.MsdynPaymentSchedule);
            SetRef(e, "msdyn_salestaxgroup",   "msdyn_taxgroup",      m.MsdynSalesTaxGroup);
            SetRef(e, "msdyn_paymentterms",    "msdyn_paymentterm",   m.MsdynPaymentTerms);
            SetRef(e, "msdyn_primarycontact",  "contact",             m.MsdynPrimaryContact);

            // msdyn_paymentday: Lookup o OptionSet segun el environment
            if (!string.IsNullOrEmpty(m.MsdynPaymentDay) && Guid.TryParse(m.MsdynPaymentDay, out var payDayGuid))
                e["msdyn_paymentday"] = new EntityReference("msdyn_paymentday", payDayGuid);

            SetString(e, "msdyn_identificationnumber", m.MsdynIdentificationNumber);
            SetString(e, "msdyn_partycountry",         m.MsdynPartyCountry);
            SetString(e, "msdyn_partystateprovince",   m.MsdynPartyStateProvince);

            // A365
            if (m.A365CreditRating.HasValue) e["a365_creditrating"] = new OptionSetValue(m.A365CreditRating.Value);
            if (m.A365OnHoldStatus.HasValue)  e["a365_onholdstatus"] = m.A365OnHoldStatus.Value;
            SetString(e, "a365_notes", m.A365Notes);

            return e;
        }

        private static void SetString(Entity e, string field, string? value)
        {
            if (!string.IsNullOrEmpty(value)) e[field] = value;
        }

        private static void SetRef(Entity e, string field, string logicalName, Guid? id)
        {
            if (id.HasValue && id.Value != Guid.Empty)
                e[field] = new EntityReference(logicalName, id.Value);
        }

        // ────────────────────────────────────────────────────────────
        // BulkAssociateRawsToMaster
        // ────────────────────────────────────────────────────────────

        private async Task BulkAssociateRawsToMasterAsync(string identificationNumber, EntityReference masterRef)
        {
            var notMasterFilter = new FilterExpression(LogicalOperator.Or);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Equal, false);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Null);

            var criteria = new FilterExpression(LogicalOperator.And);
            criteria.AddCondition(IdentificationNumber, ConditionOperator.Equal, identificationNumber);
            criteria.AddFilter(notMasterFilter);

            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet(MasterContactId),
                Criteria  = criteria,
                PageInfo  = new PagingInfo { PageNumber = 1, Count = BulkBatchSize }
            };

            var raws = (await Task.Run(() => _service.RetrieveMultiple(query))).Entities;

            _logger.LogInformation(
                "[BulkAssociate] {Count} Raws para '{Identification}' → Master {MasterId}.",
                raws.Count, identificationNumber, masterRef.Id);

            if (raws.Count == 0) return;

            if (raws.Count >= BulkBatchSize)
                _logger.LogWarning("[BulkAssociate] Limite {Limit} alcanzado. Pueden quedar Raws sin procesar.", BulkBatchSize);

            var execMultiple = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
            };

            int skip = 0;

            foreach (var raw in raws)
            {
                var current = raw.GetAttributeValue<EntityReference>(MasterContactId);
                if (current?.Id == masterRef.Id) { skip++; continue; }

                var upd = new Entity(EntityLogicalName, raw.Id);
                upd[MasterContactId] = masterRef;
                execMultiple.Requests.Add(new UpdateRequest { Target = upd });
            }

            if (execMultiple.Requests.Count == 0)
            {
                _logger.LogInformation("[BulkAssociate] Todos los {Count} Raws ya estaban asociados.", skip);
                return;
            }

            var resp = (ExecuteMultipleResponse)await Task.Run(() => _service.Execute(execMultiple));

            int errors  = resp.Responses.Count(r => r.Fault != null);
            int success = execMultiple.Requests.Count - errors;

            _logger.LogInformation(
                "[BulkAssociate] Resultado: {Success} OK, {Skip} skip, {Errors} errores.",
                success, skip, errors);

            foreach (var item in resp.Responses.Where(r => r.Fault != null))
                _logger.LogError("[BulkAssociate] Error index {Idx}: {Fault}", item.RequestIndex, item.Fault.Message);
        }
    }
}
