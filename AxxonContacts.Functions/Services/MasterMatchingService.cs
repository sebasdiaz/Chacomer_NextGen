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
        // ── Nombres logicos de entidad y campos de control ───────────
        private const string EntityLogicalName      = "contact";
        private const string IsMaster               = "axx_ismaster";
        private const string MasterContactId        = "axx_mastercontactid";
        private const string IdentificationNumber   = "msdyn_identificationnumber";
        private const int    BulkBatchSize          = 1000;

        private readonly IOrganizationService _service;
        private readonly ILogger _logger;

        public MasterMatchingService(IOrganizationService service, ILogger logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAsync(ContactEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[MasterMatchingService] Procesando Contact {ContactId} | Identification={Identification} | Trigger={Trigger}",
                message.ContactId, message.MsdynIdentificationNumber, message.TriggerMessage);

            if (message.IsMaster == true)
            {
                _logger.LogInformation("[MasterMatchingService] Contact es Master. Skip.");
                return;
            }

            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogWarning("[MasterMatchingService] IdentificationNumber vacio. Skip.");
                return;
            }

            // Re-verificar estado actual en Dataverse (el mensaje puede tener delay)
            var currentContact = await RetrieveCurrentStateAsync(message.ContactId);
            if (currentContact == null)
            {
                _logger.LogWarning(
                    "[MasterMatchingService] Contact {ContactId} no encontrado (puede haber sido eliminado).",
                    message.ContactId);
                return;
            }

            if (currentContact.GetAttributeValue<bool?>(IsMaster) == true)
            {
                _logger.LogInformation(
                    "[MasterMatchingService] Contact {ContactId} es Master en Dataverse actual. Skip.",
                    message.ContactId);
                return;
            }

            var existingMaster = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber);

            if (existingMaster != null)
            {
                _logger.LogInformation(
                    "[MasterMatchingService] Master existente {MasterId}. Asociando y propagando campos.",
                    existingMaster.Id);

                var masterRef = existingMaster.ToEntityReference();
                await AssociateRawToMasterAsync(currentContact, masterRef);
                await PropagateFieldsToMasterAsync(message, masterRef);
            }
            else
            {
                _logger.LogInformation(
                    "[MasterMatchingService] Sin Master para '{Identification}'. Creando.",
                    message.MsdynIdentificationNumber);

                var newMasterRef = await CreateMasterAsync(message);
                await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, newMasterRef);
            }

            _logger.LogInformation(
                "[MasterMatchingService] Completado. Contact={ContactId}", message.ContactId);
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
                // 0x80040217 = ObjectDoesNotExist — el contacto fue eliminado entre el evento y el procesamiento
                return null;
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
            var master = BuildContactEntity(Guid.Empty, message);
            master[IsMaster] = true;

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

                // Las sessions de Service Bus hacen esto casi imposible pero lo manejamos igual
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
        // PropagateFieldsToMaster
        // ────────────────────────────────────────────────────────────

        private async Task PropagateFieldsToMasterAsync(ContactEventMessage message, EntityReference masterRef)
        {
            var update = BuildContactEntity(masterRef.Id, message);

            if (update.Attributes.Count == 0)
            {
                _logger.LogDebug("[PropagateFields] Ningun campo a propagar al Master {Id}.", masterRef.Id);
                return;
            }

            await Task.Run(() => _service.Update(update));

            _logger.LogInformation(
                "[PropagateFields] {Count} campo(s) propagados al Master {Id}: [{Fields}]",
                update.Attributes.Count, masterRef.Id, string.Join(", ", update.Attributes.Keys));
        }

        // ────────────────────────────────────────────────────────────
        // BuildContactEntity
        // Mapea ContactEventMessage → Entity de Dataverse
        // Reutilizado por CreateMaster y PropagateFieldsToMaster
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Construye un Entity de Dataverse a partir del mensaje.
        /// Si id == Guid.Empty se usa para Create; si tiene valor, para Update.
        ///
        /// NOTAS DE IMPLEMENTACION:
        ///   - Solo se setean campos con valor (null se omite para no borrar datos en Update).
        ///   - Para campos Lookup, los logical names de las entidades relacionadas estan en
        ///     RelatedEntities (ContactConstants.cs del plugin). VERIFICAR contra metadata del env.
        ///   - msdyn_paymentday puede ser Lookup o OptionSet segun el environment.
        ///     Implementado como Lookup (EntityReference). Si es OptionSet, cambiar a OptionSetValue.
        ///   - Los campos de auditoria (modifiedby, modifiedon) NO se incluyen (Dataverse los gestiona).
        /// </summary>
        private static Entity BuildContactEntity(Guid id, ContactEventMessage m)
        {
            var e = id == Guid.Empty
                ? new Entity(EntityLogicalName)
                : new Entity(EntityLogicalName, id);

            // Datos de persona
            SetString(e, "firstname",      m.FirstName);
            SetString(e, "middlename",     m.MiddleName);
            SetString(e, "lastname",       m.LastName);
            SetString(e, "mobilephone",    m.MobilePhone);
            SetString(e, "description",    m.Description);
            SetString(e, "emailaddress1",  m.EmailAddress1);
            SetString(e, "emailaddress2",  m.EmailAddress2);
            if (m.MsdynIsProspect.HasValue) e["msdyn_isprospect"] = m.MsdynIsProspect.Value;

            // Dual Write / F&O — Lookups
            // msdyn_company nunca se propaga al master (debe quedar null)
            SetRef(e, "msdyn_partyid",          "msdyn_party",            m.MsdynPartyId);        // VERIFICAR logical name
            SetRef(e, "msdyn_customergroupid",  "msdyn_customergroup",    m.MsdynCustomerGroupId);
            SetRef(e, "transactioncurrencyid",  "transactioncurrency",    m.TransactionCurrencyId);
            SetRef(e, "msdyn_paymentschedule",  "msdyn_paymentschedule",  m.MsdynPaymentSchedule);
            SetRef(e, "msdyn_salestaxgroup",    "msdyn_salestaxgroup",    m.MsdynSalesTaxGroup);
            SetRef(e, "msdyn_paymentterms",     "msdyn_paymentterms",     m.MsdynPaymentTerms);
            SetRef(e, "msdyn_primarycontact",   "contact",                m.MsdynPrimaryContact);

            // msdyn_paymentday: VERIFICAR si es Lookup o OptionSet en tu environment
            // Si es Lookup:
            if (!string.IsNullOrEmpty(m.MsdynPaymentDay) && Guid.TryParse(m.MsdynPaymentDay, out var payDayGuid))
                e["msdyn_paymentday"] = new EntityReference("msdyn_paymentday", payDayGuid);
            // Si es OptionSet, descomentar y eliminar el bloque de arriba:
            // if (!string.IsNullOrEmpty(m.MsdynPaymentDay) && int.TryParse(m.MsdynPaymentDay, out var payDayInt))
            //     e["msdyn_paymentday"] = new OptionSetValue(payDayInt);

            // Dual Write / F&O — Booleanos y strings
            if (m.MsdynSellable.HasValue)     e["msdyn_sellable"]           = m.MsdynSellable.Value;
            SetString(e, "msdyn_identificationnumber", m.MsdynIdentificationNumber);
            SetString(e, "msdyn_partycountry",         m.MsdynPartyCountry);
            SetString(e, "msdyn_partystateprovince",   m.MsdynPartyStateProvince);

            // A365
            if (m.A365CreditRating.HasValue)  e["a365_creditrating"]  = new OptionSetValue(m.A365CreditRating.Value);
            if (m.A365OnHoldStatus.HasValue)   e["a365_onholdstatus"]  = m.A365OnHoldStatus.Value;
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
        // AssociateRawToMaster
        // ────────────────────────────────────────────────────────────

        private async Task AssociateRawToMasterAsync(Entity rawContact, EntityReference masterRef)
        {
            var current = rawContact.GetAttributeValue<EntityReference>(MasterContactId);

            if (current?.Id == masterRef.Id)
            {
                _logger.LogDebug(
                    "[MasterMatchingService] Raw {Id} ya asociado al Master {MasterId}. Skip.",
                    rawContact.Id, masterRef.Id);
                return;
            }

            var update = new Entity(EntityLogicalName, rawContact.Id);
            update[MasterContactId] = masterRef;
            await Task.Run(() => _service.Update(update));

            _logger.LogInformation(
                "[MasterMatchingService] Raw {Id} asociado al Master {MasterId}.",
                rawContact.Id, masterRef.Id);
        }

        // ────────────────────────────────────────────────────────────
        // BulkAssociateRawsToMaster
        // ────────────────────────────────────────────────────────────

        private async Task BulkAssociateRawsToMasterAsync(string identificationNumber, EntityReference masterRef)
        {
            // Los contactos de Dual Write llegan con axx_ismaster = null (campo no seteado).
            // ConditionOperator.Equal, false no matchea nulos — usamos NotEqual, true + Null en OR.
            var notMasterFilter = new FilterExpression(LogicalOperator.Or);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Equal, false);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Null);

            var criteria = new FilterExpression(LogicalOperator.And);
            criteria.AddCondition(IdentificationNumber, ConditionOperator.Equal, identificationNumber);
            criteria.AddFilter(notMasterFilter);

            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet(MasterContactId),
                Criteria = criteria,
                PageInfo = new PagingInfo { PageNumber = 1, Count = BulkBatchSize }
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

            int skip = 0, reassigned = 0;

            foreach (var raw in raws)
            {
                var current = raw.GetAttributeValue<EntityReference>(MasterContactId);
                if (current?.Id == masterRef.Id) { skip++; continue; }

                if (current != null)
                {
                    _logger.LogWarning(
                        "[BulkAssociate] Raw {Id}: reasignando de Master {Old} → {New}.",
                        raw.Id, current.Id, masterRef.Id);
                    reassigned++;
                }

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
                "[BulkAssociate] Resultado: {Success} OK ({Reassigned} reasignados), {Skip} skip, {Errors} errores.",
                success, reassigned, skip, errors);

            foreach (var item in resp.Responses.Where(r => r.Fault != null))
                _logger.LogError("[BulkAssociate] Error index {Idx}: {Fault}", item.RequestIndex, item.Fault.Message);
        }
    }
}
