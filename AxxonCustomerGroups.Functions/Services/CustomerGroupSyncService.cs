using AxxonCustomerGroups.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomerGroups.Functions.Services
{
    /// <summary>
    /// Sincroniza customer groups de F&O hacia msdyn_customergroup en Dataverse.
    ///
    /// Mapeo segun CustomerGroups_Mapping.json ([Customer groups] - [msdyn_customergroups]):
    ///   CUSTOMERGROUPID               -> msdyn_groupid
    ///   DESCRIPTION                   -> msdyn_description
    ///   ISSALESTAXINCLUDEDINPRICE     -> msdyn_issalestaxincludedinprice (yes/no -> bool)
    ///   PAYMENTTERMID                 -> msdyn_paymenttermid (lookup msdyn_paymentterm por msdyn_name)
    ///   CLEARINGPERIODPAYMENTTERMNAME -> msdyn_clearingperiodpaymenttermname (lookup msdyn_paymentterm por msdyn_name)
    ///   dataAreaId                    -> msdyn_company (lookup cdm_company por cdm_companycode)
    ///
    /// Estrategia de upsert (mismo patron que SharedProductSyncService):
    ///   1. Resuelve msdyn_company via cdm_company.cdm_companycode = dataAreaId.
    ///   2. Busca el registro existente por (msdyn_groupid, msdyn_company).
    ///      - Si existe: Update. Si no: Create.
    ///   3. Procesa en batches de ExecuteMultiple para reducir round-trips.
    ///
    /// Los payment terms se resuelven por nombre dentro de la misma compania
    /// (pueden repetirse entre companias); si no hay match con compania se
    /// reintenta solo por nombre y se loguea Warning si no existe.
    /// </summary>
    public class CustomerGroupSyncService : ICustomerGroupSyncService
    {
        private const string EntityName = "msdyn_customergroup";
        private const int    BatchSize  = 200;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<CustomerGroupSyncService> _logger;

        // Caches de lookups (validos durante una ejecucion del timer)
        private readonly Dictionary<string, Guid>  _companyCache     = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid?> _paymentTermCache = new(StringComparer.OrdinalIgnoreCase);

        public CustomerGroupSyncService(IOrganizationService orgService, ILogger<CustomerGroupSyncService> logger)
        {
            _orgService = orgService;
            _logger     = logger;
        }

        public Task SyncAsync(IReadOnlyList<FoCustomerGroup> groups, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[CustomerGroupSyncService] Iniciando sync de {Count} customer groups.", groups.Count);

            var created = 0;
            var updated = 0;
            var failed  = 0;

            for (var i = 0; i < groups.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = groups.Skip(i).Take(BatchSize).ToList();
                _logger.LogInformation(
                    "[CustomerGroupSyncService] Procesando batch {From}-{To} de {Total}.",
                    i + 1, Math.Min(i + BatchSize, groups.Count), groups.Count);

                var requests = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    }
                };

                foreach (var group in batch)
                {
                    try
                    {
                        var entity = MapToEntity(group);
                        var existingId = FindExisting(group.CustomerGroupId, group.DataAreaId);

                        if (existingId.HasValue)
                        {
                            entity.Id = existingId.Value;
                            requests.Requests.Add(new UpdateRequest { Target = entity });
                        }
                        else
                        {
                            requests.Requests.Add(new CreateRequest { Target = entity });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[CustomerGroupSyncService] Error mapeando group GroupId={GroupId} DataAreaId={Area}. Se omite.",
                            group.CustomerGroupId, group.DataAreaId);
                        failed++;
                    }
                }

                if (requests.Requests.Count == 0)
                    continue;

                var multiResponse = (ExecuteMultipleResponse)_orgService.Execute(requests);

                foreach (var responseItem in multiResponse.Responses)
                {
                    if (responseItem.Fault is not null)
                    {
                        _logger.LogError(
                            "[CustomerGroupSyncService] Fault en request index {Index}: {Message}",
                            responseItem.RequestIndex, responseItem.Fault.Message);
                        failed++;
                    }
                    else if (responseItem.Response is CreateResponse)
                        created++;
                    else
                        updated++;
                }
            }

            _logger.LogInformation(
                "[CustomerGroupSyncService] Sync completado. Creados={Created} Actualizados={Updated} Fallidos={Failed}",
                created, updated, failed);

            return Task.CompletedTask;
        }

        // ── Mapeo F&O -> Dataverse ────────────────────────────────────

        private Entity MapToEntity(FoCustomerGroup g)
        {
            var e = new Entity(EntityName);

            e["msdyn_groupid"] = g.CustomerGroupId;

            if (g.Description is not null)
                e["msdyn_description"] = g.Description;

            // Enum NoYes de F&O -> Two Options
            if (!string.IsNullOrEmpty(g.IsSalesTaxIncludedInPrice))
                e["msdyn_issalestaxincludedinprice"] =
                    g.IsSalesTaxIncludedInPrice.Equals("yes", StringComparison.OrdinalIgnoreCase);

            var companyId = ResolveCompany(g.DataAreaId);
            if (companyId.HasValue)
                e["msdyn_company"] = new EntityReference("cdm_company", companyId.Value);
            else
                _logger.LogWarning(
                    "[CustomerGroupSyncService] Compania no encontrada en Dataverse. " +
                    "cdm_companycode={DataAreaId} (GroupId={GroupId})",
                    g.DataAreaId, g.CustomerGroupId);

            SetPaymentTermLookup(e, "msdyn_paymenttermid",                  g.PaymentTermId,                 g.DataAreaId);
            SetPaymentTermLookup(e, "msdyn_clearingperiodpaymenttermname", g.ClearingPeriodPaymentTermName, g.DataAreaId);

            return e;
        }

        private void SetPaymentTermLookup(Entity e, string field, string? termName, string dataAreaId)
        {
            if (string.IsNullOrEmpty(termName))
                return;

            var termId = ResolvePaymentTerm(termName, dataAreaId);
            if (termId.HasValue)
                e[field] = new EntityReference("msdyn_paymentterm", termId.Value);
            else
                _logger.LogWarning(
                    "[CustomerGroupSyncService] Payment term no encontrado en Dataverse. " +
                    "msdyn_name={Name} (DataAreaId={Area}). Se omite el campo {Field}.",
                    termName, dataAreaId, field);
        }

        // ── Resolucion de lookups ─────────────────────────────────────

        private Guid? ResolveCompany(string dataAreaId)
        {
            if (string.IsNullOrEmpty(dataAreaId))
                return null;

            if (_companyCache.TryGetValue(dataAreaId, out var cached))
                return cached;

            var query = new QueryExpression("cdm_company")
            {
                ColumnSet = new ColumnSet(false),
                TopCount  = 1,
                NoLock    = true
            };
            query.Criteria.AddCondition("cdm_companycode", ConditionOperator.Equal, dataAreaId);

            var result = _orgService.RetrieveMultiple(query);
            if (result.Entities.Count == 0)
                return null;

            var id = result.Entities[0].Id;
            _companyCache[dataAreaId] = id;
            return id;
        }

        private Guid? ResolvePaymentTerm(string termName, string dataAreaId)
        {
            var cacheKey = $"{dataAreaId}|{termName}";
            if (_paymentTermCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // Primero por nombre + compania (los nombres pueden repetirse entre companias)
            var id = QueryPaymentTerm(termName, dataAreaId)
                     ?? QueryPaymentTerm(termName, dataAreaId: null);

            _paymentTermCache[cacheKey] = id;
            return id;
        }

        private Guid? QueryPaymentTerm(string termName, string? dataAreaId)
        {
            var query = new QueryExpression("msdyn_paymentterm")
            {
                ColumnSet = new ColumnSet(false),
                TopCount  = 1,
                NoLock    = true
            };
            query.Criteria.AddCondition("msdyn_name", ConditionOperator.Equal, termName);

            if (dataAreaId is not null)
            {
                var companyLink = query.AddLink("cdm_company", "msdyn_company", "cdm_companyid");
                companyLink.LinkCriteria.AddCondition("cdm_companycode", ConditionOperator.Equal, dataAreaId);
            }

            var result = _orgService.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0].Id : null;
        }

        private Guid? FindExisting(string customerGroupId, string dataAreaId)
        {
            try
            {
                var query = new QueryExpression(EntityName)
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount  = 1,
                    NoLock    = true
                };

                query.Criteria.AddCondition("msdyn_groupid", ConditionOperator.Equal, customerGroupId);

                var companyLink = query.AddLink("cdm_company", "msdyn_company", "cdm_companyid");
                companyLink.LinkCriteria.AddCondition("cdm_companycode", ConditionOperator.Equal, dataAreaId);

                var result = _orgService.RetrieveMultiple(query);
                return result.Entities.Count > 0 ? result.Entities[0].Id : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[CustomerGroupSyncService] Error buscando existente para GroupId={GroupId} DataArea={Area}.",
                    customerGroupId, dataAreaId);
                return null;
            }
        }
    }
}
