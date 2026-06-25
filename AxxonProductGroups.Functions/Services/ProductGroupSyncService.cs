using AxxonProductGroups.Functions.Models;
using Microsoft.Crm.Sdk.Messages;
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
    ///   dataAreaId -> msdyn_company      (lookup cdm_company por cdm_companycode)
    ///   GroupId    -> msdyn_itemgroupid  (string)
    ///   GroupName  -> msdyn_itemgroupname (string)
    ///   dataAreaId -> owningbusinessunit / owningteam
    ///                 (via AssignRequest al team por defecto de la BU cuyo name = dataAreaId)
    ///
    /// Estrategia de upsert + assign:
    ///   1. Resuelve msdyn_company via cdm_company.cdm_companycode = dataAreaId.
    ///      Si la compania no existe en Dataverse el registro se omite.
    ///   2. UpsertRequest por batch de 200. UpsertResponse.Target devuelve el EntityReference
    ///      del registro creado o actualizado sin necesidad de query previa.
    ///   3. Para cada upsert exitoso con BU resuelta, emite AssignRequest al team por defecto
    ///      de la BU (teamtype = 0). AssignRequest funciona tanto para Create como Update,
    ///      a diferencia de setear ownerid en el payload (ignorado en Update por Dataverse).
    ///   4. Los AssignRequests se envian en un segundo batch de ExecuteMultiple por batch de upsert.
    /// </summary>
    public class ProductGroupSyncService : IProductGroupSyncService
    {
        private const string EntityName = "msdyn_productgroup";
        private const int    BatchSize  = 200;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<ProductGroupSyncService> _logger;

        // Caches validos durante una ejecucion del timer
        private readonly Dictionary<string, Guid>  _companyCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid?> _buTeamCache  = new(StringComparer.OrdinalIgnoreCase);

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
            var assigned = 0;

            for (var i = 0; i < groups.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = groups.Skip(i).Take(BatchSize).ToList();
                _logger.LogInformation(
                    "[ProductGroupSyncService] Procesando batch {From}-{To} de {Total}.",
                    i + 1, Math.Min(i + BatchSize, groups.Count), groups.Count);

                // ── Paso 1: Upsert ────────────────────────────────────────────────────
                var upsertRequests = new ExecuteMultipleRequest
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

                        upsertRequests.Requests.Add(new UpsertRequest { Target = entity });
                        requestGroups.Add(group);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[ProductGroupSyncService] Error mapeando GroupId={GroupId} DataAreaId={Area}. Se omite.",
                            group.GroupId, group.DataAreaId);
                        failed++;
                    }
                }

                if (upsertRequests.Requests.Count == 0)
                    continue;

                var upsertResponse = (ExecuteMultipleResponse)_orgService.Execute(upsertRequests);

                // ── Paso 2: Recolectar IDs exitosos y preparar AssignRequests ─────────
                // UpsertResponse.Target devuelve el EntityReference del registro
                // creado o actualizado, sin necesidad de query adicional.
                var assignRequests = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    }
                };

                // Mapa de indice en assignRequests -> GroupId para logging de faults
                var assignIndexToGroupId = new Dictionary<int, string>();

                foreach (var responseItem in upsertResponse.Responses)
                {
                    if (responseItem.Fault is not null)
                    {
                        var faultedGroup = requestGroups[responseItem.RequestIndex];
                        _logger.LogError(
                            "[ProductGroupSyncService] Fault en upsert index {Index} " +
                            "(GroupId={GroupId} DataAreaId={Area}): {Message}",
                            responseItem.RequestIndex,
                            faultedGroup.GroupId,
                            faultedGroup.DataAreaId,
                            responseItem.Fault.Message);
                        failed++;
                        continue;
                    }

                    if (responseItem.Response is UpsertResponse { RecordCreated: true })
                        created++;
                    else
                        updated++;

                    // Preparar AssignRequest si hay BU resuelta para este registro
                    var successGroup = requestGroups[responseItem.RequestIndex];
                    var buTeamId     = ResolveBuDefaultTeam(successGroup.DataAreaId);
                    if (!buTeamId.HasValue)
                        continue;

                    var recordRef = ((UpsertResponse)responseItem.Response).Target;

                    assignIndexToGroupId[assignRequests.Requests.Count] = successGroup.GroupId;
                    assignRequests.Requests.Add(new AssignRequest
                    {
                        Assignee = new EntityReference("team", buTeamId.Value),
                        Target   = recordRef
                    });
                }

                // ── Paso 3: Ejecutar assigns ──────────────────────────────────────────
                if (assignRequests.Requests.Count == 0)
                    continue;

                var assignResponse = (ExecuteMultipleResponse)_orgService.Execute(assignRequests);

                foreach (var assignItem in assignResponse.Responses)
                {
                    if (assignItem.Fault is not null)
                    {
                        assignIndexToGroupId.TryGetValue(assignItem.RequestIndex, out var groupId);
                        _logger.LogError(
                            "[ProductGroupSyncService] Fault en assign index {Index} " +
                            "(GroupId={GroupId}): {Message}",
                            assignItem.RequestIndex, groupId, assignItem.Fault.Message);
                    }
                    else
                        assigned++;
                }
            }

            _logger.LogInformation(
                "[ProductGroupSyncService] Sync completado. " +
                "Creados={Created} Actualizados={Updated} Asignados={Assigned} Fallidos={Failed}",
                created, updated, assigned, failed);

            return Task.CompletedTask;
        }

        // ── Mapeo F&O -> Dataverse ────────────────────────────────────────────────

        private Entity? MapToEntity(FoProductGroup g)
        {
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

            // Alternate key (msdyn_itemgroupid + msdyn_company)
            e.KeyAttributes["msdyn_itemgroupid"] = g.GroupId;
            e.KeyAttributes["msdyn_company"]     = companyRef;

            e["msdyn_itemgroupid"]   = g.GroupId;
            e["msdyn_company"]       = companyRef;

            if (g.GroupName is not null)
                e["msdyn_itemgroupname"] = g.GroupName;

            return e;
        }

        // ── Resolucion de lookups ─────────────────────────────────────────────────

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

        /// <summary>
        /// Resuelve el team por defecto (teamtype = 0, Owner) de la businessunit
        /// cuyo name coincide con dataAreaId. Se usa como Assignee en AssignRequest
        /// para que Dataverse derive owningbusinessunit y owningteam correctamente.
        /// </summary>
        private Guid? ResolveBuDefaultTeam(string dataAreaId)
        {
            if (string.IsNullOrEmpty(dataAreaId))
                return null;

            if (_buTeamCache.TryGetValue(dataAreaId, out var cached))
                return cached;

            var buQuery = new QueryExpression("businessunit")
            {
                ColumnSet = new ColumnSet("businessunitid"),
                TopCount  = 1,
                NoLock    = true
            };
            buQuery.Criteria.AddCondition("name", ConditionOperator.Equal, dataAreaId);

            var buResult = _orgService.RetrieveMultiple(buQuery);
            if (buResult.Entities.Count == 0)
            {
                _logger.LogWarning(
                    "[ProductGroupSyncService] BusinessUnit no encontrada. name={DataAreaId}",
                    dataAreaId);
                _buTeamCache[dataAreaId] = null;
                return null;
            }

            var buId = buResult.Entities[0].Id;

            var teamQuery = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet(false),
                TopCount  = 1,
                NoLock    = true
            };
            teamQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, buId);
            teamQuery.Criteria.AddCondition("teamtype",       ConditionOperator.Equal, 0);

            var teamResult = _orgService.RetrieveMultiple(teamQuery);
            if (teamResult.Entities.Count == 0)
            {
                _logger.LogWarning(
                    "[ProductGroupSyncService] Team por defecto no encontrado para BU={BuId} (DataAreaId={DataAreaId}).",
                    buId, dataAreaId);
                _buTeamCache[dataAreaId] = null;
                return null;
            }

            var teamId = teamResult.Entities[0].Id;
            _buTeamCache[dataAreaId] = teamId;
            return teamId;
        }
    }
}
