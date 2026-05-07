using System.Text.Json;
using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Azure Function disparada por mensajes en la queue de Service Bus.
    ///
    /// Sessions deshabilitadas (IsSessionsEnabled = false):
    ///   - La queue actual no tiene sessions habilitadas.
    ///   - Para habilitar ordering por msdyn_identificationnumber, recrear la queue
    ///     con "Enable sessions: true" y cambiar IsSessionsEnabled a true.
    ///
    /// Retry policy:
    ///   - Dataverse gestiona los reintentos del System Job (plugin).
    ///   - Service Bus gestiona los reintentos del mensaje (Lock Duration + Max Delivery Count).
    ///   - Si la Function falla despues de Max Delivery Count (3), el mensaje va al DLQ.
    ///   - El mensaje del DLQ debe procesarse manualmente o con una Function separada de DLQ handler.
    ///
    /// autoComplete = false (configurado en host.json):
    ///   - El mensaje se completa manualmente via messageActions.CompleteMessageAsync().
    ///   - Esto garantiza que el mensaje no se pierde si la Function falla antes de completar.
    /// </summary>
    public class ContactMasterMatchingFunction
    {
        private readonly MasterMatchingService _matchingService;
        private readonly ILogger<ContactMasterMatchingFunction> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

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
                        "[ContactMasterMatchingFunction] No se pudo deserializar el mensaje {MessageId}. " +
                        "Enviando a DLQ.",
                        messageId);

                    await messageActions.DeadLetterMessageAsync(
                        message,
                        deadLetterReason: "DeserializationFailed",
                        deadLetterErrorDescription: "El cuerpo del mensaje no es un ContactEventMessage valido.");
                    return;
                }

                // 2. Procesar
                await _matchingService.ProcessAsync(payload);

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

        private static ContactEventMessage? DeserializeMessage(ServiceBusReceivedMessage message)
        {
            try
            {
                var body = message.Body.ToString();
                return JsonSerializer.Deserialize<ContactEventMessage>(body, _jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Error deserializando ContactEventMessage: {ex.Message}", ex);
            }
        }
    }
}
