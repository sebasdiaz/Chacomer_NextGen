using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Azure Function disparada por el Service Endpoint de Dataverse via Service Bus (cola "account").
    /// Deserializa el RemoteExecutionContext, crea el Account Master y linkea el raw como hijo.
    ///
    /// Sessions deshabilitadas (IsSessionsEnabled = false):
    ///   - Para habilitar ordering por msdyn_identificationnumber, recrear la queue
    ///     con "Enable sessions: true" y cambiar IsSessionsEnabled a true.
    ///
    /// Retry policy:
    ///   - Service Bus gestiona los reintentos (Lock Duration + Max Delivery Count).
    ///   - Si la Function falla despues de Max Delivery Count, el mensaje va al DLQ.
    ///
    /// autoComplete = false (configurado en host.json):
    ///   - El mensaje se completa manualmente via messageActions.CompleteMessageAsync().
    ///   - La renovacion del lock es manejada automaticamente por el host (maxAutoRenewDuration en host.json).
    /// </summary>
    public class AccountMasterMatchingFunction
    {
        private readonly AccountProcessingService _processingService;
        private readonly ILogger<AccountMasterMatchingFunction> _logger;

        public AccountMasterMatchingFunction(
            AccountProcessingService processingService,
            ILogger<AccountMasterMatchingFunction> logger)
        {
            _processingService = processingService;
            _logger            = logger;
        }

        [Function(nameof(AccountMasterMatchingFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%AccountServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions)
        {
            var messageId     = message.MessageId;
            var sessionId     = message.SessionId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[AccountMasterMatchingFunction] Mensaje recibido. MessageId={MessageId} | " +
                "SessionId={SessionId} | DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, sessionId, deliveryCount, message.EnqueuedTime);

            AccountEventMessage? payload = null;

            try
            {
                // 1. Deserializar el RemoteExecutionContext
                payload = DeserializeMessage(message);

                if (payload == null)
                {
                    _logger.LogError(
                        "[AccountMasterMatchingFunction] No se pudo parsear el RemoteExecutionContext {MessageId}. " +
                        "Enviando a DLQ.",
                        messageId);

                    await messageActions.DeadLetterMessageAsync(
                        message,
                        deadLetterReason: "ParseFailed",
                        deadLetterErrorDescription: "El cuerpo del mensaje no es un RemoteExecutionContext valido.");
                    return;
                }

                // 2. Crear/localizar master, linkear raws y validar RUC contra SET
                await _processingService.ProcessAsync(payload);

                // 3. Completar el mensaje (autoComplete = false)
                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    "[AccountMasterMatchingFunction] Mensaje {MessageId} procesado correctamente.",
                    messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[AccountMasterMatchingFunction] Error procesando mensaje {MessageId} " +
                    "(SessionId={SessionId}, DeliveryCount={DeliveryCount}): {Error}",
                    messageId, sessionId, deliveryCount, ex.Message);

                await messageActions.AbandonMessageAsync(message);
                throw;
            }
        }

        private static AccountEventMessage? DeserializeMessage(ServiceBusReceivedMessage message)
        {
            try
            {
                return AccountExecutionContextParser.Parse(message.Body.ToString());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error procesando RemoteExecutionContext: {ex.Message}", ex);
            }
        }
    }
}
