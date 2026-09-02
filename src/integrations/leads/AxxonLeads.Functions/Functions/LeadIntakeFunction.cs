using System.ServiceModel;
using System.Text.Json;
using Axxon.Eip.Core.Messaging;
using AxxonLeads.Functions.Models;
using AxxonLeads.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonLeads.Functions.Functions
{
    /// <summary>
    /// Crea leads en Dataverse a partir de la cola <c>lead-intake</c>, que alimentan
    /// Thinkchat y el resto de los satelites.
    ///
    /// El mensaje es un envelope EiP con <see cref="LeadIntakePayload"/> adentro. La cola
    /// es la puerta de entrada: el satelite no habla con Dataverse ni conoce sus logical
    /// names — manda un contrato estable y se desentiende de si Dataverse esta arriba.
    ///
    /// <b>Sin sessions</b>, a diferencia de <c>customer-fo-sync</c>: cada mensaje crea un
    /// lead independiente, no hay dos eventos del mismo registro que puedan cruzarse. Poner
    /// sessions serializaria todo el intake sin que nada lo necesite.
    ///
    /// autoComplete = false (host.json): el mensaje se completa a mano.
    ///
    /// Manejo de mensajes, segun el contrato de errores de la plataforma:
    ///   - Cuerpo que no deserializa -> DLQ (<c>DeserializationFailed</c>).
    ///   - Faltan obligatorios del lead -> DLQ (<c>ContractViolation</c>), con el campo
    ///     que falta en la descripcion para que el satelite pueda corregir.
    ///   - Dataverse rechaza por datos (columna inexistente, optionset invalido) -> DLQ
    ///     (<c>BusinessRuleFailed</c>): reintentar no lo arregla.
    ///   - Errores transitorios (Dataverse caido, throttling) -> Abandon para que Service
    ///     Bus reintente; tras Max Delivery Count el mensaje va al DLQ.
    /// </summary>
    public class LeadIntakeFunction
    {
        private readonly ILeadIntakeService _intakeService;
        private readonly ILogger<LeadIntakeFunction> _logger;

        public LeadIntakeFunction(
            ILeadIntakeService intakeService,
            ILogger<LeadIntakeFunction> logger)
        {
            _intakeService = intakeService;
            _logger        = logger;
        }

        [Function(nameof(LeadIntakeFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%LeadIntakeServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions,
            CancellationToken         cancellationToken)
        {
            var messageId = message.MessageId;

            _logger.LogInformation(
                "[LeadIntakeFunction] Mensaje recibido. MessageId={MessageId} | " +
                "DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, message.DeliveryCount, message.EnqueuedTime);

            EipMessage? envelope;

            try
            {
                envelope = JsonSerializer.Deserialize<EipMessage>(
                    message.Body.ToString(), EipMessageDefaults.SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[LeadIntakeFunction] El cuerpo del mensaje {MessageId} no es un envelope EiP " +
                    "valido. Enviando a DLQ.", messageId);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.DeserializationFailed,
                    deadLetterErrorDescription: ex.Message);
                return;
            }

            var contractError = LeadEnvelopeValidator.Validate(envelope, out var payload);

            if (contractError is not null)
            {
                _logger.LogError(
                    "[LeadIntakeFunction] Mensaje {MessageId} incompleto: {Error}. Enviando a DLQ.",
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
                    "[LeadIntakeFunction] Procesando lead de {Source} " +
                    "(externalId={ExternalId}, operacion={Operation}, correlationId={CorrelationId}).",
                    envelope!.Source, payload!.ExternalId ?? "(sin id de origen)",
                    envelope.Operation, envelope.CorrelationId);

                var result = await _intakeService.ProcessAsync(
                    envelope.Source, payload, cancellationToken);

                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    result.AlreadyExisted
                        ? "[LeadIntakeFunction] Mensaje {MessageId} completado sin crear: el lead " +
                          "{LeadId} ya existia."
                        : "[LeadIntakeFunction] Mensaje {MessageId} procesado correctamente. " +
                          "Lead {LeadId} creado.",
                    messageId, result.LeadId);
            }
            catch (NonRetryableLeadException ex)
            {
                _logger.LogError(ex,
                    "[LeadIntakeFunction] Error de datos no reintentable en el mensaje {MessageId}: " +
                    "{Error}. Enviando a DLQ.", messageId, ex.Message);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.BusinessRuleFailed,
                    deadLetterErrorDescription: ex.Message);
            }
            catch (FaultException<OrganizationServiceFault> ex) when (IsPermanent(ex))
            {
                // Dataverse contesta con fault tanto por un dato invalido como por estar
                // sobrecargado. Solo los primeros van al DLQ: los de throttling se
                // reintentan, que es exactamente lo que Dataverse esta pidiendo.
                _logger.LogError(ex,
                    "[LeadIntakeFunction] Dataverse rechazo el mensaje {MessageId} " +
                    "(ErrorCode={ErrorCode}). Enviando a DLQ sin reintentar. Dataverse dijo: {Detail}",
                    messageId, ex.Detail.ErrorCode, ex.Detail.Message);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.BusinessRuleFailed,
                    deadLetterErrorDescription: ex.Detail.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[LeadIntakeFunction] Error procesando el mensaje {MessageId} " +
                    "(DeliveryCount={DeliveryCount}): {Error}",
                    messageId, message.DeliveryCount, ex.Message);

                await messageActions.AbandonMessageAsync(message);

                // Re-lanzar para que Application Insights registre la excepcion.
                throw;
            }
        }

        /// <summary>
        /// Un fault de Dataverse es permanente salvo que sea de proteccion del servicio
        /// (limites de API) o de concurrencia. Esos dos se resuelven solos esperando, y
        /// mandarlos al DLQ perderia leads por una ventana de throttling.
        /// </summary>
        private static bool IsPermanent(FaultException<OrganizationServiceFault> ex) =>
            ex.Detail.ErrorCode is not (
                -2147015902 or  // NumberOfRequestsExceeded
                -2147015903 or  // TimeLimitExceeded
                -2147015898 or  // ConcurrencyLimitExceeded
                -2147204784);   // SqlTimeout
    }
}
