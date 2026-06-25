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
    ///   dataAreaId -> ownerid (team por defecto de la businessunit cuyo name = dataAreaId)
    ///
    /// Estrategia de upsert:
    ///   1. Resuelve msdyn_company via cdm_company.cdm_companycode = dataAreaId.
    ///      Si la compania no existe en Dataverse el registro se omite.
    ///   2. Resuelve el team por defecto (teamtype=0) de la businessunit cuyo name = dataAreaId.
    ///      owningbusinessunit es calculado por Dataverse a partir de ownerid, por lo que
    ///      se setea ownerid apuntando al team de la BU destino. Si no se encuentra la BU
    ///      o su team, el registro se upsertea sin ownerid (queda en la BU del caller).
    ///   3. UpsertRequest con KeyAttributes contra la alternate key
    ///      (msdyn_itemgroupid + msdyn_company): Dataverse decide Create vs Update
    ///      en el servidor, sin query previa por registro.
    ///   4. Procesa en batches de ExecuteMultiple para reducir round-trips.
    /// </summary>
    public class ProductGroupSyncService : IProductGroupSyncService
    {
        private const string EntityName = "msdyn_productgroup";
        private const int    BatchSize  = 200;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<ProductGroupSyncService> _logger;

        // Caches validos durante una ejecucion del timer
        private readonly Dictionary<string, Guid>  _companyCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid?>  _buTeamCache  = new(StringComparer.OrdinalIgnoreCase);

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

            // owningbusinessunit es calculado por Dataverse a partir de ownerid.
            // Se asigna el team por defecto de la BU cuyo name = dataAreaId.
            var buTeamId = ResolveBuDefaultTeam(g.DataAreaId);
            if (buTeamId.HasValue)
                e["ownerid"] = new EntityReference("team", buTeamId.Value);
            else
                _logger.LogWarning(
                    "[ProductGroupSyncService] Team de BU no encontrado para DataAreaId={DataAreaId} " +
                    "(GroupId={GroupId}). El registro quedara en la BU del caller.",
                    g.DataAreaId, g.GroupId);

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

        /// <summary>
        /// Resuelve el team por defecto (teamtype = 0, Owner) de la businessunit
        /// cuyo name coincide con dataAreaId. Cada BU tiene exactamente un team
        /// por defecto con el mismo nombre; ese team es el owner correcto para
        /// que owningbusinessunit quede apuntando a la BU destino.
        /// </summary>
        private Guid? ResolveBuDefaultTeam(string dataAreaId)
        {
            if (string.IsNullOrEmpty(dataAreaId))
                return null;

            if (_buTeamCache.TryGetValue(dataAreaId, out var cached))
                return cached;

            // Busca la BU por name = dataAreaId
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

            // Busca el team por defecto de esa BU (teamtype = 0 = Owner)
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
