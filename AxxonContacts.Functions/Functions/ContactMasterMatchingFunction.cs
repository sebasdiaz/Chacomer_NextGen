using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Azure Function disparada por el Service Endpoint de Dataverse via Service Bus.
    /// Recibe el RemoteExecutionContext nativo (JSON) y aplica la logica master/raw.
    ///
    /// Sessions deshabilitadas (IsSessionsEnabled = false):
    ///   - La queue actual no tiene sessions habilitadas.
    ///   - Para habilitar ordering por msdyn_identificationnumber, recrear la queue
    ///     con "Enable sessions: true" y cambiar IsSessionsEnabled a true.
    ///
    /// Retry policy:
    ///   - Service Bus gestiona los reintentos (Lock Duration + Max Delivery Count).
    ///   - Si la Function falla despues de Max Delivery Count, el mensaje va al DLQ.
    ///
    /// autoComplete = false (configurado en host.json):
    ///   - El mensaje se completa manualmente via messageActions.CompleteMessageAsync().
    /// </summary>
    public class ContactMasterMatchingFunction
    {
        private readonly MasterMatchingService _matchingService;
        private readonly ILogger<ContactMasterMatchingFunction> _logger;

        public ContactMasterMatchingFunction(
            MasterMatchingService matchingService,
            ILogger<ContactMasterMatchingFunction> logger)
        {
            _matchingService = matchingService;
            _logger = logger;
        }

        [Function(nameof(ContactMasterMatchingFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%ServiceBusQueueName%",
                Connection = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            var messageId = message.MessageId;
            var sessionId = message.SessionId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[ContactMasterMatchingFunction] Mensaje recibido. MessageId={MessageId} | " +
                "SessionId={SessionId} | DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, sessionId, deliveryCount, message.EnqueuedTime);

            ContactEventMessage? payload = null;

            try
            {
                // 1. Deserializar el payload
                payload = DeserializeMessage(message);

                if (payload == null)
                {
                    _logger.LogError(
                        "[ContactMasterMatchingFunction] No se pudo parsear el RemoteExecutionContext {MessageId}. " +
                        "Enviando a DLQ.",
                        messageId);

                    await messageActions.DeadLetterMessageAsync(
                        message,
                        deadLetterReason: "ParseFailed",
                        deadLetterErrorDescription: "El cuerpo del mensaje no es un RemoteExecutionContext valido.");
                    return;
                }

                // 2. Renovar el lock periodicamente mientras se procesa para evitar MessageLockLost
                using var cts = new CancellationTokenSource();
                var renewTask = RenewLockPeriodicallyAsync(message, messageActions, cts.Token);

                try
                {
                    await _matchingService.ProcessAsync(payload);
                }
                finally
                {
                    cts.Cancel();
                    await renewTask;
                }

                // 3. Completar el mensaje (autoComplete = false)
                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    "[ContactMasterMatchingFunction] Mensaje {MessageId} procesado correctamente.",
                    messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ContactMasterMatchingFunction] Error procesando mensaje {MessageId} " +
                    "(SessionId={SessionId}, DeliveryCount={DeliveryCount}): {Error}",
                    messageId, sessionId, deliveryCount, ex.Message);

                // No completamos el mensaje: Service Bus lo reencola automaticamente
                // hasta alcanzar Max Delivery Count (configurado en la queue), luego va al DLQ.

                // Abandonar el mensaje para que Service Bus lo reintente inmediatamente
                // (en lugar de esperar que expire el Lock Duration)
                await messageActions.AbandonMessageAsync(message);

                // Re-lanzar para que Application Insights registre la excepcion
                throw;
            }
        }

        private async Task RenewLockPeriodicallyAsync(
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    if (cancellationToken.IsCancellationRequested) break;

                    await messageActions.RenewMessageLockAsync(message, cancellationToken);
                    _logger.LogDebug(
                        "[ContactMasterMatchingFunction] Lock renovado para mensaje {MessageId}.",
                        message.MessageId);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[ContactMasterMatchingFunction] Error renovando lock del mensaje {MessageId}.",
                    message.MessageId);
            }
        }

        private static ContactEventMessage? DeserializeMessage(ServiceBusReceivedMessage message)
        {
            try
            {
                return ExecutionContextParser.Parse(message.Body.ToString());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error procesando RemoteExecutionContext: {ex.Message}", ex);
            }
        }
    }
}
