using AxxonProductGroups.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonProductGroups.Functions.Services
{
    /// <summary>
    /// Sincroniza product groups de F&O hacia msdyn_productgroup en Dataverse.
    ///
    /// Mapeo (integracion unidireccional F&O -> Dataverse):
    ///   dataAreaId -> msdyn_company (lookup cdm_company por cdm_companycode)
    ///   GroupId    -> msdyn_itemgroupid (string)
    ///   GroupName  -> msdyn_itemgroupname (string)
    ///
    /// Estrategia de upsert:
    ///   1. Resuelve msdyn_company via cdm_company.cdm_companycode = dataAreaId.
    ///      Si la compania no existe en Dataverse el registro se omite: es parte
    ///      de la clave y no puede upsertearse sin ella.
    ///   2. UpsertRequest con KeyAttributes contra la alternate key
    ///      (msdyn_itemgroupid + msdyn_company): Dataverse decide Create vs Update
    ///      en el servidor, sin query previa por registro.
    ///   3. Procesa en batches de ExecuteMultiple para reducir round-trips.
    /// </summary>
    public class ProductGroupSyncService : IProductGroupSyncService
    {
        private const string EntityName = "msdyn_productgroup";
        private const int    BatchSize  = 200;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<ProductGroupSyncService> _logger;

        // Cache de companies valido durante una ejecucion del timer
        private readonly Dictionary<string, Guid> _companyCache = new(StringComparer.OrdinalIgnoreCase);

        public ProductGroupSyncService(IOrganizationService orgService, ILogger<ProductGroupSyncService> logger)
        {
            _orgService = orgService;
            _logger     = logger;
        }

        public Task SyncAsync(IReadOnlyList<FoProductGroup> groups, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[ProductGroupSyncService] Iniciando sync de {Count} product groups.", groups.Count);

            var created = 0;
            var updated = 0;
            var failed  = 0;

            for (var i = 0; i < groups.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = groups.Skip(i).Take(BatchSize).ToList();
                _logger.LogInformation(
                    "[ProductGroupSyncService] Procesando batch {From}-{To} de {Total}.",
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

                var requestGroups = new List<FoProductGroup>();

                foreach (var group in batch)
                {
                    try
                    {
                        var entity = MapToEntity(group);
                        if (entity is null)
                        {
                            failed++;
                            continue;
                        }

                        requests.Requests.Add(new UpsertRequest { Target = entity });
                        requestGroups.Add(group);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[ProductGroupSyncService] Error mapeando group GroupId={GroupId} DataAreaId={Area}. Se omite.",
                            group.GroupId, group.DataAreaId);
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
                        var faultedGroup = requestGroups[responseItem.RequestIndex];
                        _logger.LogError(
                            "[ProductGroupSyncService] Fault en request index {Index} " +
                            "(GroupId={GroupId} DataAreaId={Area}): {Message}",
                            responseItem.RequestIndex,
                            faultedGroup.GroupId,
                            faultedGroup.DataAreaId,
                            responseItem.Fault.Message);
                        failed++;
                    }
                    else if (responseItem.Response is UpsertResponse { RecordCreated: true })
                        created++;
                    else
                        updated++;
                }
            }

            _logger.LogInformation(
                "[ProductGroupSyncService] Sync completado. Creados={Created} Actualizados={Updated} Fallidos={Failed}",
                created, updated, failed);

            return Task.CompletedTask;
        }

        // ── Mapeo F&O -> Dataverse ────────────────────────────────────

        private Entity? MapToEntity(FoProductGroup g)
        {
            // msdyn_company integra la alternate key: sin compania resuelta
            // no hay clave de upsert y el registro se omite.
            var companyId = ResolveCompany(g.DataAreaId);
            if (!companyId.HasValue)
            {
                _logger.LogWarning(
                    "[ProductGroupSyncService] Compania no encontrada en Dataverse. " +
                    "cdm_companycode={DataAreaId} (GroupId={GroupId}). Se omite el registro.",
                    g.DataAreaId, g.GroupId);
                return null;
            }

            var companyRef = new EntityReference("cdm_company", companyId.Value);

            var e = new Entity(EntityName);

            // Alternate key (msdyn_itemgroupid + msdyn_company): Dataverse resuelve
            // Create vs Update en el servidor via UpsertRequest.
            e.KeyAttributes["msdyn_itemgroupid"] = g.GroupId;
            e.KeyAttributes["msdyn_company"]     = companyRef;

            e["msdyn_itemgroupid"]   = g.GroupId;
            e["msdyn_company"]       = companyRef;

            if (g.GroupName is not null)
                e["msdyn_itemgroupname"] = g.GroupName;

            return e;
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
    }
}
