using AxxonProducts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Microsoft.Extensions.Logging;

namespace AxxonProducts.Functions.Functions
{
    /// <summary>
    /// Timer Trigger que corre cada hora y sincroniza ReleasedProductsV2 de F&O
    /// hacia msdyn_sharedproductdetails en Dataverse.
    ///
    /// Schedule: "%Schedules:ReleasedProductSync%" — configurable por App Setting.
    ///   Valor por defecto: "0 0 * * * *" (cada hora en punto).
    ///
    /// Si el timer esta atrasado (IsPastDue = true) igual ejecuta el sync
    /// para no perder datos; se loguea como Warning para visibilidad.
    ///
    /// Lectura de F&O: OData cross-company con paginacion automatica.
    /// Escritura en Dataverse: upsert por (msdyn_itemnumber + msdyn_companyid)
    ///   en batches de ExecuteMultiple de 200 registros.
    /// </summary>
    public class ReleasedProductSyncFunction
    {
        private readonly IFoDataService _foDataService;
        private readonly ISharedProductSyncService _syncService;
        private readonly ILogger<ReleasedProductSyncFunction> _logger;

        public ReleasedProductSyncFunction(
            IFoDataService foDataService,
            ISharedProductSyncService syncService,
            ILogger<ReleasedProductSyncFunction> logger)
        {
            _foDataService = foDataService;
            _syncService   = syncService;
            _logger        = logger;
        }

        [Function(nameof(ReleasedProductSyncFunction))]
        public async Task Run(
            [TimerTrigger("%Schedules:ReleasedProductSync%")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            if (timer.IsPastDue)
            {
                _logger.LogWarning(
                    "[ReleasedProductSyncFunction] Timer ejecutado con retraso. " +
                    "Se procesa igual para no perder datos.");
            }

            _logger.LogInformation(
                "[ReleasedProductSyncFunction] Iniciando sync de ReleasedProductsV2. " +
                "ScheduledTime={ScheduledTime} ActualTime={ActualTime}",
                timer.ScheduleStatus?.Next, DateTimeOffset.UtcNow);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var totalRead = 0;

            try
            {
                const int windowSize = 1000;
                var window = new List<Models.FoReleasedProduct>(windowSize);

                await foreach (var product in _foDataService.GetReleasedProductsAsync(cancellationToken))
                {
                    window.Add(product);
                    totalRead++;

                    if (window.Count >= windowSize)
                    {
                        await _syncService.SyncAsync(window, cancellationToken);
                        window.Clear();
                    }
                }

                if (window.Count > 0)
                    await _syncService.SyncAsync(window, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "[ReleasedProductSyncFunction] Sync completado. " +
                    "TotalLeidos={Total} Duracion={Elapsed}ms",
                    totalRead, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "[ReleasedProductSyncFunction] Sync cancelado por CancellationToken " +
                    "despues de leer {Count} registros.",
                    totalRead);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "[ReleasedProductSyncFunction] Error en sync de ReleasedProductsV2 " +
                    "despues de {Elapsed}ms. TotalLeidos={Total}",
                    stopwatch.ElapsedMilliseconds, totalRead);
                throw;
            }
        }
    }
}
