using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Aplica un lote de updates con ExecuteMultiple y reintenta los que fallan.
    ///
    /// Existe por un caso real: escribir el link raw→master dispara el plugin
    /// sincronico de Dual Write y, si F&amp;O esta throttleando, el update falla.
    /// Con ContinueOnError = true ese fallo solo se logueaba, el mensaje se
    /// completaba igual y el link se perdia para siempre — sin reintento y sin
    /// quedar en el DLQ, o sea sin ninguna forma de enterarse salvo mirando el
    /// registro.
    ///
    /// Ahora los que fallan se reintentan con backoff y, si siguen fallando, se
    /// lanza. Reprocesar el mensaje es idempotente (el master ya existe y los raws
    /// ya linkeados se saltan), asi que dejar que Service Bus reintente es seguro:
    /// en el peor caso el mensaje termina en el DLQ, visible, en vez de evaporarse.
    /// </summary>
    public static class BulkUpdateExecutor
    {
        /// <summary>
        /// Backoff entre reintentos. Corto a proposito: el throttling de F&amp;O suele
        /// durar segundos, y el lock del mensaje se renueva hasta 10 minutos
        /// (maxAutoRenewDuration en host.json). Si el ERP sigue caido despues de
        /// esto, el reintento que sirve es el de Service Bus, no otro loop aca.
        /// </summary>
        private static readonly TimeSpan[] DefaultRetryDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)
        ];

        /// <summary>
        /// Aplica los updates y no vuelve hasta que todos hayan pasado.
        /// Lanza <see cref="InvalidOperationException"/> si alguno sigue fallando
        /// despues de agotar los reintentos.
        /// </summary>
        public static async Task ExecuteAsync(
            IOrganizationService service,
            ILogger logger,
            string logPrefix,
            IReadOnlyList<Entity> updates,
            IReadOnlyList<TimeSpan>? retryDelays = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(updates);

            if (updates.Count == 0) return;

            var delays    = retryDelays ?? DefaultRetryDelays;
            var pending   = updates;
            string? lastFault = null;

            for (var attempt = 0; ; attempt++)
            {
                var (succeeded, failed, fault) =
                    await ExecuteBatchAsync(service, pending, cancellationToken);

                lastFault = fault ?? lastFault;

                logger.LogInformation(
                    "{Prefix} Intento {Attempt}: {Succeeded} OK, {Errors} errores.",
                    logPrefix, attempt + 1, succeeded, failed.Count);

                if (failed.Count == 0) return;

                if (attempt >= delays.Count)
                    throw new InvalidOperationException(
                        $"{logPrefix} {failed.Count} de {updates.Count} updates siguen fallando " +
                        $"tras {attempt + 1} intentos. Ultimo error: {lastFault}");

                logger.LogWarning(
                    "{Prefix} {Count} updates fallaron ({Fault}). Reintentando en {Delay}.",
                    logPrefix, failed.Count, lastFault, delays[attempt]);

                await Task.Delay(delays[attempt], cancellationToken);
                pending = failed;
            }
        }

        private static async Task<(int Succeeded, IReadOnlyList<Entity> Failed, string? LastFault)>
            ExecuteBatchAsync(
                IOrganizationService service,
                IReadOnlyList<Entity> updates,
                CancellationToken cancellationToken)
        {
            var request = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                // ContinueOnError: un raw que falla no puede impedir que se apliquen
                // los demas. Los que fallaron vuelven en el reintento.
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
            };

            foreach (var update in updates)
                request.Requests.Add(new UpdateRequest { Target = update });

            var response = (ExecuteMultipleResponse)await Task.Run(
                () => service.Execute(request), cancellationToken);

            if (response.Responses is null) return (updates.Count, [], null);

            var failed = new List<Entity>();
            string? lastFault = null;

            foreach (var item in response.Responses)
            {
                if (item.Fault is null) continue;

                failed.Add(updates[item.RequestIndex]);
                lastFault = item.Fault.Message;
            }

            return (updates.Count - failed.Count, failed, lastFault);
        }
    }
}
