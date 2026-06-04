using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;

namespace AxxonContacts.Functions.Services
{
    public class AccountMasterMatchingService
    {
        private const string EntityLogicalName    = "account";
        private const string IsMaster             = "axx_ismaster";
        private const string MasterAccountId      = "axx_masteraccountid";
        private const string IdentificationNumber = "msdyn_identificationnumber";
        private const int    BulkBatchSize        = 1000;

        private readonly IOrganizationService _service;
        private readonly ILogger              _logger;

        public AccountMasterMatchingService(IOrganizationService service, ILogger logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Solo procesa eventos Create de accounts raw (no master).
        /// Si ya existe un master para el msdyn_identificationnumber → linkea los raws pendientes y retorna su referencia.
        /// Si no existe → crea el master, linkea todos los raws y retorna la referencia del nuevo master.
        /// Retorna null si el evento se ignora (trigger distinto a Create, es master, o sin identification).
        /// </summary>
        public async Task<EntityReference?> ProcessAsync(AccountEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[AccountMasterMatchingService] Procesando Account {AccountId} | Identification={Identification} | Trigger={Trigger}",
                message.AccountId, message.MsdynIdentificationNumber, message.TriggerMessage);

            bool isCreate = string.Equals(message.TriggerMessage, "Create", StringComparison.OrdinalIgnoreCase);
            bool isUpdateWithNewIdentification = string.Equals(message.TriggerMessage, "Update", StringComparison.OrdinalIgnoreCase)
                                                 && message.IdentificationNumberChanged;

            if (!isCreate && !isUpdateWithNewIdentification)
            {
                _logger.LogInformation(
                    "[AccountMasterMatchingService] Evento '{Trigger}' ignorado (IdentificationChanged={Changed}).",
                    message.TriggerMessage, message.IdentificationNumberChanged);
                return null;
            }

            if (message.IsMaster)
            {
                _logger.LogInformation("[AccountMasterMatchingService] Account es Master. Skip.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogWarning("[AccountMasterMatchingService] IdentificationNumber vacio. Skip.");
                return null;
            }

            var existingMaster = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber);
            if (existingMaster != null)
            {
                _logger.LogInformation(
                    "[AccountMasterMatchingService] Master {MasterId} ya existe para '{Identification}'. Linkeando raws pendientes.",
                    existingMaster.Id, message.MsdynIdentificationNumber);
                await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, existingMaster.ToEntityReference());
                return existingMaster.ToEntityReference();
            }

            _logger.LogInformation(
                "[AccountMasterMatchingService] Sin Master para '{Identification}'. Creando.",
                message.MsdynIdentificationNumber);

            var newMasterRef = await CreateMasterAsync(message);
            await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, newMasterRef);

            _logger.LogInformation(
                "[AccountMasterMatchingService] Completado. Account={AccountId} | Master={MasterId}",
                message.AccountId, newMasterRef.Id);

            return newMasterRef;
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
                    "[AccountMasterMatchingService] {Count} Masters para '{Identification}'. Usando el primero ({Id}).",
                    results.Entities.Count, identificationNumber, results.Entities[0].Id);

            return results.Entities.Count > 0 ? results.Entities[0] : null;
        }

        // ────────────────────────────────────────────────────────────
        // CreateMaster
        // ────────────────────────────────────────────────────────────

        private async Task<EntityReference> CreateMasterAsync(AccountEventMessage message)
        {
            var master = BuildMasterEntity(message);

            try
            {
                var masterId = await Task.Run(() => _service.Create(master));
                _logger.LogInformation("[AccountMasterMatchingService] Master creado. Id={Id}", masterId);
                return new EntityReference(EntityLogicalName, masterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[AccountMasterMatchingService] Create fallo: {Error}. Re-buscando (posible race condition).",
                    ex.Message);

                var existing = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber!);
                if (existing != null)
                {
                    _logger.LogInformation("[AccountMasterMatchingService] Race condition resuelta. Master={Id}", existing.Id);
                    return existing.ToEntityReference();
                }
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────
        // BuildMasterEntity
        // ────────────────────────────────────────────────────────────

        private static Entity BuildMasterEntity(AccountEventMessage m)
        {
            var e = new Entity(EntityLogicalName);

            e[IsMaster] = true;

            // name es ApplicationRequired: usar identification como fallback si viene vacío
            var masterName = !string.IsNullOrEmpty(m.Name)
                ? m.Name
                : m.MsdynIdentificationNumber;
            SetString(e, "name", masterName);

            SetString(e, "telephone1",    m.Telephone1);
            SetString(e, "emailaddress1", m.EmailAddress1);
            SetString(e, "description",   m.Description);
            SetString(e, IdentificationNumber, m.MsdynIdentificationNumber);

            return e;
        }

        private static void SetString(Entity e, string field, string? value)
        {
            if (!string.IsNullOrEmpty(value)) e[field] = value;
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
                ColumnSet = new ColumnSet(MasterAccountId),
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
                var current = raw.GetAttributeValue<EntityReference>(MasterAccountId);
                if (current?.Id == masterRef.Id) { skip++; continue; }

                var upd = new Entity(EntityLogicalName, raw.Id);
                upd[MasterAccountId] = masterRef;
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
