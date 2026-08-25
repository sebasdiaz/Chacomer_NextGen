using System.Text.Json;
using Axxon.Eip.Core.FinOps;
using Axxon.Eip.Core.Messaging;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Functions
{
    /// <summary>
    /// Sincroniza hacia <c>LTMCustTable</c> la contraparte de localizacion PY del cliente,
    /// una vez que el customer existe en F&amp;O.
    ///
    /// <b>Por que es una cola aparte y no un paso mas de CustomerFoSyncFunction</b> (ADR-001):
    /// un codigo de localizacion invalido no tiene que re-martillar el alta del customer, y
    /// —lo decisivo— en las legal entities que SI estan en Dual Write las modificaciones las
    /// hace Dual Write y nunca pasan por nuestras Functions, con lo cual la fila quedaria
    /// congelada en el alta.
    ///
    /// Sessions habilitadas: la session es el id del registro, para que dos modificaciones
    /// del mismo cliente no se procesen fuera de orden.
    ///
    /// autoComplete = false (host.json): el mensaje se completa a mano.
    /// </summary>
    public class LtmCustSyncFunction
    {
        private readonly ILtmCustSyncService _syncService;
        private readonly ILogger<LtmCustSyncFunction> _logger;

        public LtmCustSyncFunction(
            ILtmCustSyncService syncService,
            ILogger<LtmCustSyncFunction> logger)
        {
            _syncService = syncService;
            _logger      = logger;
        }

        [Function(nameof(LtmCustSyncFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%LtmSyncServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = true)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions,
            CancellationToken         cancellationToken)
        {
            var messageId = message.MessageId;

            _logger.LogInformation(
                "[LtmCustSyncFunction] Mensaje recibido. MessageId={MessageId} | SessionId={SessionId} | " +
                "DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, message.SessionId, message.DeliveryCount, message.EnqueuedTime);

            EipMessage? envelope;

            try
            {
                envelope = JsonSerializer.Deserialize<EipMessage>(
                    message.Body.ToString(), EipMessageDefaults.SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[LtmCustSyncFunction] El cuerpo del mensaje {MessageId} no es un envelope EiP " +
                    "valido. Enviando a DLQ.", messageId);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.DeserializationFailed,
                    deadLetterErrorDescription: ex.Message);
                return;
            }

            var contractError = Validate(envelope, out var entityType, out var payload);

            if (contractError is not null)
            {
                _logger.LogError(
                    "[LtmCustSyncFunction] Mensaje {MessageId} incompleto: {Error}. Enviando a DLQ.",
                    messageId, contractError);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.ContractViolation,
                    deadLetterErrorDescription: contractError);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "[LtmCustSyncFunction] Procesando {EntityType} {RecordId} " +
                    "(operacion={Operation}, dataAreaId={DataAreaId}, correlationId={CorrelationId}).",
                    entityType, payload!.RecordId, envelope!.Operation,
                    payload.DataAreaId ?? "sin resolver", envelope.CorrelationId);

                var synced = await _syncService.ProcessAsync(
                    entityType!, payload.RecordId, cancellationToken);

                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    synced
                        ? "[LtmCustSyncFunction] Mensaje {MessageId} procesado correctamente."
                        : "[LtmCustSyncFunction] Mensaje {MessageId} completado sin sincronizar " +
                          "(el customer todavia no existe en F&O).",
                    messageId);
            }
            catch (NonRetryableSyncException ex)
            {
                _logger.LogError(ex,
                    "[LtmCustSyncFunction] Error de datos no reintentable en el mensaje {MessageId}: " +
                    "{Error}. Enviando a DLQ.", messageId, ex.Message);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.ContractViolation,
                    deadLetterErrorDescription: ex.Message);
            }
            catch (FoODataException ex) when (ex.IsPermanent)
            {
                // Un 400 de F&O es una regla de negocio violada (decision #4): tipicamente un
                // codigo de localizacion que no existe en la legal entity destino.
                _logger.LogError(ex,
                    "[LtmCustSyncFunction] F&O rechazo el mensaje {MessageId} con HTTP {Status}. " +
                    "Enviando a DLQ sin reintentar. F&O dijo: {FoMessage}",
                    messageId, (int)ex.Status, ex.Detail);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.BusinessRuleFailed,
                    deadLetterErrorDescription: ex.Detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[LtmCustSyncFunction] Error procesando el mensaje {MessageId} " +
                    "(DeliveryCount={DeliveryCount}): {Error}",
                    messageId, message.DeliveryCount, ex.Message);

                await messageActions.AbandonMessageAsync(message);

                // Re-lanzar para que Application Insights registre la excepcion.
                throw;
            }
        }

        /// <summary>
        /// Chequea el contrato del envelope. Devuelve el motivo del rechazo, o null si el
        /// mensaje esta completo.
        /// </summary>
        private static string? Validate(
            EipMessage? envelope,
            out string? entityType,
            out CustomerSyncPayload? payload)
        {
            entityType = null;
            payload    = null;

            if (envelope is null)
                return "El cuerpo del mensaje deserializo en null.";

            if (string.IsNullOrWhiteSpace(envelope.EntityType))
                return "El envelope no trae 'entityType'.";

            entityType = envelope.EntityType;

            try
            {
                payload = envelope.GetPayload<CustomerSyncPayload>();
            }
            catch (JsonException ex)
            {
                return $"El payload no es un CustomerSyncPayload valido: {ex.Message}";
            }

            if (payload is null)
                return "El envelope no trae 'payload'.";

            if (payload.RecordId == Guid.Empty)
                return "El payload no trae 'recordId'.";

            return null;
        }
    }
}
