using AxxonContacts.Functions.Configuration;
using AxxonContacts.Functions.Models;
using AxxonContacts.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Functions
{
    /// <summary>
    /// Azure Function que valida el msdyn_identificationnumber contra la API de TURUC
    /// y actualiza el contacto con el RUC validado, el estado fiscal y la respuesta completa.
    ///
    /// IMPORTANTE — Fan-out con ContactMasterMatchingFunction:
    ///   Ambas functions consumen la misma queue. En desarrollo esto es aceptable
    ///   para pruebas de cada function por separado. En producción se recomienda
    ///   migrar a un Service Bus Topic con dos Subscriptions independientes:
    ///     - master-matching  → ContactMasterMatchingFunction
    ///     - ruc-validation   → ContactRucValidationFunction
    ///   Esto garantiza que cada mensaje sea procesado por ambas functions.
    ///
    /// Retry policy / autoComplete: igual que ContactMasterMatchingFunction.
    /// </summary>
    public class ContactRucValidationFunction
    {
        private readonly RucValidationService _validationService;
        private readonly ServiceBusClient     _sbClient;
        private readonly AppSettings          _settings;
        private readonly ILogger<ContactRucValidationFunction> _logger;

        public ContactRucValidationFunction(
            RucValidationService validationService,
            ServiceBusClient sbClient,
            AppSettings settings,
            ILogger<ContactRucValidationFunction> logger)
        {
            _validationService = validationService;
            _sbClient          = sbClient;
            _settings          = settings;
            _logger            = logger;
        }

        [Function(nameof(ContactRucValidationFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%RucValidationQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            var messageId     = message.MessageId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[ContactRucValidationFunction] Mensaje recibido. MessageId={MessageId} | " +
                "DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, deliveryCount, message.EnqueuedTime);

            ContactEventMessage? payload = null;

            try
            {
                // 1. Deserializar el RemoteExecutionContext
                payload = DeserializeMessage(message);

                if (payload == null)
                {
                    _logger.LogError(
                        "[ContactRucValidationFunction] No se pudo parsear el RemoteExecutionContext {MessageId}. " +
                        "Enviando a DLQ.",
                        messageId);

                    await messageActions.DeadLetterMessageAsync(
                        message,
                        deadLetterReason: "ParseFailed",
                        deadLetterErrorDescription: "El cuerpo del mensaje no es un RemoteExecutionContext valido.");
                    return;
                }

                // 2. Renovar el lock periodicamente mientras se procesa
                await using var receiver = _sbClient.CreateReceiver(
                    _settings.RucValidationQueueName,
                    new ServiceBusReceiverOptions { PrefetchCount = 0 });
                using var cts       = new CancellationTokenSource();
                var       renewTask = RenewLockPeriodicallyAsync(message, receiver, cts.Token);

                try
                {
                    await _validationService.ProcessAsync(payload);
                }
                finally
                {
                    cts.Cancel();
                    await renewTask;
                }

                // 3. Completar el mensaje (autoComplete = false)
                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    "[ContactRucValidationFunction] Mensaje {MessageId} procesado correctamente.",
                    messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ContactRucValidationFunction] Error procesando mensaje {MessageId} " +
                    "(DeliveryCount={DeliveryCount}): {Error}",
                    messageId, deliveryCount, ex.Message);

                await messageActions.AbandonMessageAsync(message);
                throw;
            }
        }

        /// <summary>
        /// Renueva el lock cada 30s usando el SDK directo (AMQP) para evitar el error gRPC del host.
        /// </summary>
        private async Task RenewLockPeriodicallyAsync(
            ServiceBusReceivedMessage message,
            ServiceBusReceiver receiver,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    if (cancellationToken.IsCancellationRequested) break;

                    await receiver.RenewMessageLockAsync(message, cancellationToken);
                    _logger.LogDebug(
                        "[ContactRucValidationFunction] Lock renovado para mensaje {MessageId}.",
                        message.MessageId);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[ContactRucValidationFunction] Error renovando lock del mensaje {MessageId}.",
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
