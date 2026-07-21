using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Azure Function disparada por el Service Endpoint de Dataverse via Service Bus.
    /// Deserializa el RemoteExecutionContext y delega el procesamiento a ContactProcessingService.
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
    ///   - La renovacion del lock es manejada automaticamente por el host (maxAutoRenewDuration en host.json).
    /// </summary>
    public class ContactMasterMatchingFunction
    {
        private readonly ContactProcessingService _processingService;
        private readonly ILogger<ContactMasterMatchingFunction> _logger;

        public ContactMasterMatchingFunction(
            ContactProcessingService processingService,
            ILogger<ContactMasterMatchingFunction> logger)
        {
            _processingService = processingService;
            _logger            = logger;
        }

        [Function(nameof(ContactMasterMatchingFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%ServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions)
        {
            var messageId     = message.MessageId;
            var sessionId     = message.SessionId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[ContactMasterMatchingFunction] Mensaje recibido. MessageId={MessageId} | " +
                "SessionId={SessionId} | DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, sessionId, deliveryCount, message.EnqueuedTime);

            ContactEventMessage? payload = null;

            try
            {
                // 1. Deserializar el RemoteExecutionContext
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

                // 2. Orquestar master matching + validacion RUC
                await _processingService.ProcessAsync(payload);

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

                // Abandonar para que Service Bus reintente inmediatamente
                await messageActions.AbandonMessageAsync(message);

                // Re-lanzar para que Application Insights registre la excepcion
                throw;
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
