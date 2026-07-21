using AxxonCustomerGroups.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerGroups.Functions.Functions
{
    /// <summary>
    /// Timer Trigger que corre una vez por dia y sincroniza CustomerGroups de F&O
    /// hacia msdyn_customergroup en Dataverse.
    ///
    /// Schedule: "%Schedules:CustomerGroupSync%" — configurable por App Setting.
    ///   Valor por defecto: "0 0 23 * * *" (todos los dias a las 23:00).
    ///   El CRON corre en UTC salvo que la Function App tenga WEBSITE_TIME_ZONE
    ///   configurado (ej. "Paraguay Standard Time" para las 23:00 de Asuncion).
    ///
    /// Si el timer esta atrasado (IsPastDue = true) igual ejecuta el sync
    /// para no perder datos; se loguea como Warning para visibilidad.
    ///
    /// Lectura de F&O: OData cross-company con paginacion automatica.
    /// Escritura en Dataverse: upsert por (msdyn_groupid + msdyn_company)
    ///   en batches de ExecuteMultiple de 200 registros.
    /// </summary>
    public class CustomerGroupSyncFunction
    {
        private readonly IFoCustomerGroupService _foCustomerGroupService;
        private readonly ICustomerGroupSyncService _syncService;
        private readonly ILogger<CustomerGroupSyncFunction> _logger;

        public CustomerGroupSyncFunction(
            IFoCustomerGroupService foCustomerGroupService,
            ICustomerGroupSyncService syncService,
            ILogger<CustomerGroupSyncFunction> logger)
        {
            _foCustomerGroupService = foCustomerGroupService;
            _syncService            = syncService;
            _logger                 = logger;
        }

        [Function(nameof(CustomerGroupSyncFunction))]
        public async Task Run(
            [TimerTrigger("%Schedules:CustomerGroupSync%")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            if (timer.IsPastDue)
            {
                _logger.LogWarning(
                    "[CustomerGroupSyncFunction] Timer ejecutado con retraso. " +
                    "Se procesa igual para no perder datos.");
            }

            _logger.LogInformation(
                "[CustomerGroupSyncFunction] Iniciando sync de CustomerGroups. " +
                "ScheduledTime={ScheduledTime} ActualTime={ActualTime}",
                timer.ScheduleStatus?.Next, DateTimeOffset.UtcNow);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var totalRead = 0;

            try
            {
                // CustomerGroups es un dataset chico (decenas de registros por compania);
                // se materializa completo y se sincroniza en una sola pasada.
                var groups = new List<Models.FoCustomerGroup>();

                await foreach (var group in _foCustomerGroupService.GetCustomerGroupsAsync(cancellationToken))
                {
                    groups.Add(group);
                    totalRead++;
                }

                if (groups.Count > 0)
                    await _syncService.SyncAsync(groups, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "[CustomerGroupSyncFunction] Sync completado. " +
                    "TotalLeidos={Total} Duracion={Elapsed}ms",
                    totalRead, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "[CustomerGroupSyncFunction] Sync cancelado por CancellationToken " +
                    "despues de leer {Count} registros.",
                    totalRead);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "[CustomerGroupSyncFunction] Error en sync de CustomerGroups " +
                    "despues de {Elapsed}ms. TotalLeidos={Total}",
                    stopwatch.ElapsedMilliseconds, totalRead);
                throw;
            }
        }
    }
}
