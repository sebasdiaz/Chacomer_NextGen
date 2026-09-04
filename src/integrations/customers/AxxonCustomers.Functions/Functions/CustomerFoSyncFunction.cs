using System.Text.Json;
using Axxon.Eip.Core.Dataverse;
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
    /// Sincroniza hacia F&amp;O los accounts y contacts de las legal entities que
    /// <b>no</b> sincroniza Dual Write, usando el mismo mapeo que usaria Dual Write pero
    /// por la API OData.
    ///
    /// El productor es AxxonContacts.Functions, que publica en esta cola despues del
    /// master matching cuando <c>cdm_isenabledfordualwrite</c> de la company es false.
    /// El reparto es excluyente: si la legal entity SI esta en Dual Write, este flujo no
    /// ve el registro (lo maneja Dual Write, y el alta del lead calificado la hace
    /// <see cref="QualifyLeadCustomerSyncFunction"/>).
    ///
    /// Sessions habilitadas: la session es el id del registro, para que dos
    /// modificaciones del mismo account/contact no se procesen fuera de orden y dejen
    /// F&amp;O con el valor viejo.
    ///
    /// autoComplete = false (host.json): el mensaje se completa a mano.
    /// </summary>
    public class CustomerFoSyncFunction
    {
        private readonly ICustomerSyncService _syncService;
        private readonly ILogger<CustomerFoSyncFunction> _logger;

        public CustomerFoSyncFunction(
            ICustomerSyncService syncService,
            ILogger<CustomerFoSyncFunction> logger)
        {
            _syncService = syncService;
            _logger      = logger;
        }

        [Function(nameof(CustomerFoSyncFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%FoSyncServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = true)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions,
            CancellationToken         cancellationToken)
        {
            var messageId     = message.MessageId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[CustomerFoSyncFunction] Mensaje recibido. MessageId={MessageId} | " +
                "SessionId={SessionId} | DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, message.SessionId, deliveryCount, message.EnqueuedTime);

            EipMessage? envelope;

            try
            {
                envelope = JsonSerializer.Deserialize<EipMessage>(
                    message.Body.ToString(), EipMessageDefaults.SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[CustomerFoSyncFunction] El cuerpo del mensaje {MessageId} no es un envelope " +
                    "EiP valido. Enviando a DLQ.", messageId);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.DeserializationFailed,
                    deadLetterErrorDescription: ex.Message);
                return;
            }

            var contractError = Validate(envelope, out var mapName, out var payload);

            if (contractError is not null)
            {
                _logger.LogError(
                    "[CustomerFoSyncFunction] Mensaje {MessageId} incompleto: {Error}. Enviando a DLQ.",
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
                    "[CustomerFoSyncFunction] Procesando {EntityType} {RecordId} " +
                    "(operacion={Operation}, dataAreaId={DataAreaId}, correlationId={CorrelationId}).",
                    mapName, payload!.RecordId, envelope!.Operation,
                    payload.DataAreaId ?? "sin resolver", envelope.CorrelationId);

                // Todo lo que llega a esta cola es de una legal entity FUERA de Dual Write:
                // FoSyncDispatcher solo publica cuando el handling es Api. Por eso se pasa
                // fijo, sin volver a resolver la company — y por eso aca no aplica la guarda
                // syncWhen del overlay (ver FoPayloadBuilder.ShouldSync).
                await _syncService.ProcessAsync(
                    mapName!, payload.RecordId, CompanySyncHandling.Api, cancellationToken);

                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    "[CustomerFoSyncFunction] Mensaje {MessageId} procesado correctamente.", messageId);
            }
            catch (NonRetryableSyncException ex)
            {
                _logger.LogError(ex,
                    "[CustomerFoSyncFunction] Error de datos no reintentable en el mensaje " +
                    "{MessageId}: {Error}. Enviando a DLQ.", messageId, ex.Message);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.ContractViolation,
                    deadLetterErrorDescription: ex.Message);
            }
            catch (FoODataException ex) when (ex.IsPermanent)
            {
                // Un 400 de F&O es una regla de negocio violada: reintentar solo consume
                // delivery count y martilla el ERP.
                _logger.LogError(ex,
                    "[CustomerFoSyncFunction] F&O rechazo el mensaje {MessageId} con HTTP {Status}. " +
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
                    "[CustomerFoSyncFunction] Error procesando el mensaje {MessageId} " +
                    "(DeliveryCount={DeliveryCount}): {Error}",
                    messageId, deliveryCount, ex.Message);

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
            out string? mapName,
            out CustomerSyncPayload? payload)
        {
            mapName = null;
            payload = null;

            if (envelope is null)
                return "El cuerpo del mensaje deserializo en null.";

            if (string.IsNullOrWhiteSpace(envelope.EntityType))
                return "El envelope no trae 'entityType'.";

            // El entityType es el nombre del mapeo: el registry avisa si no existe.
            mapName = envelope.EntityType;

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
