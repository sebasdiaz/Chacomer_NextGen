using AxxonThinkchat.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>
    /// Sincroniza los templates de Thinkchat hacia la tabla axx_metatemplates.
    ///
    /// Estrategia:
    ///   1. UpsertRequest con KeyAttributes contra la alternate key axx_id:
    ///      Dataverse resuelve Create vs Update del lado del servidor, sin query
    ///      previa por registro. Mismo patron que CustomerGroupSyncService.
    ///   2. Batches de ExecuteMultiple de 200 con ContinueOnError: un template
    ///      fallido no corta el sync.
    ///   3. Barrido de desactivacion: los registros activos cuyo axx_id no vino
    ///      en la corrida pasan a statecode = Inactive. No se borran: el historial
    ///      de que el template existio se conserva.
    ///
    /// Guarda importante: si la API devuelve cero templates NO se desactiva nada.
    /// Un response vacio es indistinguible de una falla silenciosa del lado de
    /// Thinkchat, y desactivar la tabla entera por un hipo de red no es aceptable.
    /// </summary>
    public class TemplateSyncService : ITemplateSyncService
    {
        private const string EntityName    = "axx_metatemplates";
        private const string IdField       = "axx_id";
        private const int    BatchSize     = 200;

        // statecode/statuscode estandar de una tabla custom.
        private const int StateActive    = 0;
        private const int StateInactive  = 1;
        private const int StatusInactive = 2;

        private readonly IOrganizationService _orgService;
        private readonly ILogger<TemplateSyncService> _logger;

        public TemplateSyncService(IOrganizationService orgService, ILogger<TemplateSyncService> logger)
        {
            _orgService = orgService;
            _logger     = logger;
        }

        public Task SyncAsync(
            IReadOnlyList<ThinkchatTemplate> templates,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[TemplateSyncService] Iniciando sync de {Count} templates.", templates.Count);

            var seenIds = Upsert(templates, cancellationToken);

            if (seenIds.Count == 0)
            {
                _logger.LogWarning(
                    "[TemplateSyncService] Ningun template con axx_id valido en esta corrida. " +
                    "Se omite el barrido de desactivacion para no desactivar la tabla entera.");
                return Task.CompletedTask;
            }

            Deactivate(seenIds, cancellationToken);

            return Task.CompletedTask;
        }

        // ── Upsert ────────────────────────────────────────────────────

        private HashSet<string> Upsert(
            IReadOnlyList<ThinkchatTemplate> templates,
            CancellationToken cancellationToken)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var created = 0;
            var updated = 0;
            var failed  = 0;

            for (var i = 0; i < templates.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = templates.Skip(i).Take(BatchSize).ToList();

                var requests = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    }
                };

                // Paralela a requests.Requests: los templates omitidos corren los
                // indices, asi que RequestIndex no apunta al batch original.
                var requestTemplates = new List<ThinkchatTemplate>();

                foreach (var template in batch)
                {
                    if (string.IsNullOrWhiteSpace(template.Id))
                    {
                        _logger.LogWarning(
                            "[TemplateSyncService] Template sin id (Name={Name}). Se omite: " +
                            "el id es la clave del upsert.",
                            template.Name);
                        failed++;
                        continue;
                    }

                    requests.Requests.Add(new UpsertRequest { Target = MapToEntity(template) });
                    requestTemplates.Add(template);
                    seenIds.Add(template.Id.Trim());
                }

                if (requests.Requests.Count == 0)
                    continue;

                var multiResponse = (ExecuteMultipleResponse)_orgService.Execute(requests);

                foreach (var responseItem in multiResponse.Responses)
                {
                    if (responseItem.Fault is not null)
                    {
                        var faulted = requestTemplates[responseItem.RequestIndex];
                        _logger.LogError(
                            "[TemplateSyncService] Fault en request index {Index} " +
                            "(Id={Id} Name={Name}): {Message}",
                            responseItem.RequestIndex, faulted.Id, faulted.Name,
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
                "[TemplateSyncService] Upsert completado. Creados={Created} Actualizados={Updated} Fallidos={Failed}",
                created, updated, failed);

            return seenIds;
        }

        private static Entity MapToEntity(ThinkchatTemplate t)
        {
            var e = new Entity(EntityName);

            // Alternate key sobre axx_id: Dataverse decide Create vs Update.
            e.KeyAttributes[IdField] = t.Id!.Trim();

            e[IdField]         = t.Id!.Trim();
            e["axx_name"]      = t.Name;
            e["axx_category"]  = t.Category;
            e["axx_status"]    = t.Status;
            e["axx_type"]      = t.Type;
            e["axx_text"]      = t.Text;
            e["axx_variables"] = t.VariablesAsText;

            return e;
        }

        // ── Barrido de desactivacion ──────────────────────────────────

        private void Deactivate(HashSet<string> seenIds, CancellationToken cancellationToken)
        {
            var stale = QueryActiveNotIn(seenIds, cancellationToken);

            if (stale.Count == 0)
            {
                _logger.LogInformation(
                    "[TemplateSyncService] No hay templates para desactivar.");
                return;
            }

            _logger.LogInformation(
                "[TemplateSyncService] {Count} templates activos ya no vienen de Thinkchat. Se desactivan.",
                stale.Count);

            var deactivated = 0;
            var failed      = 0;

            for (var i = 0; i < stale.Count; i += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = stale.Skip(i).Take(BatchSize).ToList();

                var requests = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    }
                };

                foreach (var (id, externalId) in batch)
                {
                    var e = new Entity(EntityName, id)
                    {
                        ["statecode"]  = new OptionSetValue(StateInactive),
                        ["statuscode"] = new OptionSetValue(StatusInactive)
                    };

                    requests.Requests.Add(new UpdateRequest { Target = e });
                    _logger.LogInformation(
                        "[TemplateSyncService] Desactivando template axx_id={ExternalId} ({Id}).",
                        externalId, id);
                }

                var multiResponse = (ExecuteMultipleResponse)_orgService.Execute(requests);

                foreach (var responseItem in multiResponse.Responses)
                {
                    if (responseItem.Fault is not null)
                    {
                        var (id, externalId) = batch[responseItem.RequestIndex];
                        _logger.LogError(
                            "[TemplateSyncService] Fault desactivando axx_id={ExternalId} ({Id}): {Message}",
                            externalId, id, responseItem.Fault.Message);
                        failed++;
                    }
                    else
                        deactivated++;
                }
            }

            _logger.LogInformation(
                "[TemplateSyncService] Desactivacion completada. Desactivados={Count} Fallidos={Failed}",
                deactivated, failed);
        }

        /// <summary>
        /// Trae los registros activos y filtra en memoria los que no vinieron.
        /// El filtrado no va server-side a proposito: un NotIn con miles de valores
        /// no entra en una query, y el universo de templates es de decenas.
        /// </summary>
        private List<(Guid Id, string? ExternalId)> QueryActiveNotIn(
            HashSet<string> seenIds,
            CancellationToken cancellationToken)
        {
            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(IdField),
                NoLock    = true,
                PageInfo  = new PagingInfo { Count = 500, PageNumber = 1 }
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);

            var stale = new List<(Guid, string?)>();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = _orgService.RetrieveMultiple(query);

                foreach (var entity in page.Entities)
                {
                    var externalId = entity.GetAttributeValue<string>(IdField);

                    // Un registro sin axx_id no es identificable contra Thinkchat: puede
                    // ser carga manual. No se toca — desactivar lo que no se puede
                    // matchear es peor que dejarlo activo.
                    if (string.IsNullOrWhiteSpace(externalId))
                    {
                        _logger.LogWarning(
                            "[TemplateSyncService] Registro {Id} sin axx_id. Se deja como esta.",
                            entity.Id);
                        continue;
                    }

                    if (!seenIds.Contains(externalId.Trim()))
                        stale.Add((entity.Id, externalId));
                }

                if (!page.MoreRecords)
                    break;

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            return stale;
        }
    }
}
