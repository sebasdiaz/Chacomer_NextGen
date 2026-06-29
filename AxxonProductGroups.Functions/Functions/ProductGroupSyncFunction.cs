using AxxonProductGroups.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AxxonProductGroups.Functions.Functions
{
    /// <summary>
    /// Timer Trigger que sincroniza ProductGroups de F&O hacia msdyn_productgroup en Dataverse.
    ///
    /// Integracion unidireccional: F&O -> Dataverse.
    ///
    /// Schedule: "%Schedules:ProductGroupSync%" — configurable por App Setting.
    ///   Valor por defecto: "0 0 23 * * *" (todos los dias a las 23:00 UTC).
    ///   Ajustar WEBSITE_TIME_ZONE en la Function App si se requiere zona local.
    ///
    /// Lectura de F&O: OData cross-company con paginacion automatica.
    /// Escritura en Dataverse: upsert por (msdyn_itemgroupid + msdyn_company)
    ///   en batches de ExecuteMultiple de 200 registros.
    /// </summary>
    public class ProductGroupSyncFunction
    {
        private readonly IFoProductGroupService _foProductGroupService;
        private readonly IProductGroupSyncService _syncService;
        private readonly ILogger<ProductGroupSyncFunction> _logger;

        public ProductGroupSyncFunction(
            IFoProductGroupService foProductGroupService,
            IProductGroupSyncService syncService,
            ILogger<ProductGroupSyncFunction> logger)
        {
            _foProductGroupService = foProductGroupService;
            _syncService           = syncService;
            _logger                = logger;
        }

        [Function(nameof(ProductGroupSyncFunction))]
        public async Task Run(
            [TimerTrigger("%ExecutionInterval%", RunOnStartup = false)] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            if (timer.IsPastDue)
            {
                _logger.LogWarning(
                    "[ProductGroupSyncFunction] Timer ejecutado con retraso. " +
                    "Se procesa igual para no perder datos.");
            }

            _logger.LogInformation(
                "Iniciando sync de ProductGroups. " +
                "ScheduledTime={ScheduledTime} ActualTime={ActualTime}",
                timer.ScheduleStatus?.Next, DateTimeOffset.UtcNow);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var totalRead = 0;

            try
            {
                var groups = new List<Models.FoProductGroup>();

                await foreach (var group in _foProductGroupService.GetProductGroupsAsync(cancellationToken))
                {
                    groups.Add(group);
                    totalRead++;
                }

                _logger.LogInformation(
                    "[ProductGroupSyncFunction] Lectura de ProductGroups completada. " +
                    "TotalLeidos={Total} Duracion={Elapsed}ms",
                    totalRead, stopwatch.ElapsedMilliseconds);

                if (groups.Count > 0)
                    await _syncService.SyncAsync(groups, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "[ProductGroupSyncFunction] Sync completado. " +
                    "TotalLeidos={Total} Duracion={Elapsed}ms",
                    totalRead, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "[ProductGroupSyncFunction] Sync cancelado por CancellationToken " +
                    "despues de leer {Count} registros.",
                    totalRead);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "[ProductGroupSyncFunction] Error en sync de ProductGroups " +
                    "despues de {Elapsed}ms. TotalLeidos={Total}",
                    stopwatch.ElapsedMilliseconds, totalRead);
                throw;
            }
        }
    }
}
