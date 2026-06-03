using AxxonContacts.Functions.Configuration;
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
    /// </summary>
    public class AccountMasterMatchingFunction
    {
        private readonly AccountMasterMatchingService _matchingService;
        private readonly ServiceBusClient             _sbClient;
        private readonly AppSettings                  _settings;
        private readonly ILogger<AccountMasterMatchingFunction> _logger;

        public AccountMasterMatchingFunction(
            AccountMasterMatchingService matchingService,
            ServiceBusClient             sbClient,
            AppSettings                  settings,
            ILogger<AccountMasterMatchingFunction> logger)
        {
            _matchingService = matchingService;
            _sbClient        = sbClient;
            _settings        = settings;
            _logger          = logger;
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

                // 2. Renovar el lock periodicamente mientras se procesa
                await using var receiver  = _sbClient.CreateReceiver(
                    _settings.AccountServiceBusQueueName,
                    new ServiceBusReceiverOptions { PrefetchCount = 0 });
                using var cts       = new CancellationTokenSource();
                var       renewTask = RenewLockPeriodicallyAsync(message, receiver, cts.Token);

                try
                {
                    // 3. Crear master y linkear el raw
                    await _matchingService.ProcessAsync(payload);
                }
                finally
                {
                    cts.Cancel();
                    await renewTask;
                }

                // 4. Completar el mensaje (autoComplete = false)
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

        /// <summary>
        /// Renueva el lock cada 30s usando el SDK directo para evitar el error gRPC "Unimplemented".
        /// </summary>
        private async Task RenewLockPeriodicallyAsync(
            ServiceBusReceivedMessage message,
            ServiceBusReceiver        receiver,
            CancellationToken         cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    if (cancellationToken.IsCancellationRequested) break;

                    await receiver.RenewMessageLockAsync(message, cancellationToken);
                    _logger.LogDebug(
                        "[AccountMasterMatchingFunction] Lock renovado para mensaje {MessageId}.",
                        message.MessageId);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AccountMasterMatchingFunction] Error renovando lock del mensaje {MessageId}.",
                    message.MessageId);
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
