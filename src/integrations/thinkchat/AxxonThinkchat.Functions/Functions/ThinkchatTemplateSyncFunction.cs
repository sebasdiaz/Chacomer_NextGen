using AxxonThinkchat.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AxxonThinkchat.Functions.Functions
{
    /// <summary>
    /// Timer Trigger que corre cada dos horas y sincroniza los templates de Thinkchat
    /// hacia la tabla axx_metatemplates de Dataverse.
    ///
    /// Schedule: "%Schedules:ThinkchatTemplateSync%" — configurable por App Setting.
    ///   Valor sugerido: "0 0 */2 * * *" (a las 00, 02, 04... en punto).
    ///   El CRON corre en UTC salvo que la Function App tenga WEBSITE_TIME_ZONE.
    ///   Con un ciclo de 2 horas la zona horaria casi no importa, pero conviene
    ///   dejarla seteada para que los logs coincidan con la hora local.
    ///
    /// Si el timer esta atrasado (IsPastDue = true) igual ejecuta el sync; se loguea
    /// como Warning para visibilidad.
    /// </summary>
    public class ThinkchatTemplateSyncFunction
    {
        private readonly IThinkchatTemplateService _templateService;
        private readonly ITemplateSyncService _syncService;
        private readonly ILogger<ThinkchatTemplateSyncFunction> _logger;

        public ThinkchatTemplateSyncFunction(
            IThinkchatTemplateService templateService,
            ITemplateSyncService syncService,
            ILogger<ThinkchatTemplateSyncFunction> logger)
        {
            _templateService = templateService;
            _syncService     = syncService;
            _logger          = logger;
        }

        [Function(nameof(ThinkchatTemplateSyncFunction))]
        public async Task Run(
            [TimerTrigger("%Schedules:ThinkchatTemplateSync%")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            if (timer.IsPastDue)
            {
                _logger.LogWarning(
                    "[ThinkchatTemplateSyncFunction] Timer ejecutado con retraso. " +
                    "Se procesa igual para no perder datos.");
            }

            _logger.LogInformation(
                "[ThinkchatTemplateSyncFunction] Iniciando sync de templates. " +
                "ScheduledTime={ScheduledTime} ActualTime={ActualTime}",
                timer.ScheduleStatus?.Next, DateTimeOffset.UtcNow);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var templates = await _templateService.GetTemplatesAsync(cancellationToken);

                await _syncService.SyncAsync(templates, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "[ThinkchatTemplateSyncFunction] Sync completado. " +
                    "TotalLeidos={Total} Duracion={Elapsed}ms",
                    templates.Count, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[ThinkchatTemplateSyncFunction] Sync cancelado por CancellationToken.");
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "[ThinkchatTemplateSyncFunction] Error en sync de templates despues de {Elapsed}ms.",
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
