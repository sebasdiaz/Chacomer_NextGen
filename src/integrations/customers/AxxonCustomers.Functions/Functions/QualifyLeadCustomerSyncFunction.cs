using System.Net;
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
    /// Azure Function disparada por el Service Endpoint de Dataverse (mensaje QualifyLead)
    /// via la cola "leadcontacts" del Service Bus.
    ///
    /// Flujo:
    ///   1. Parsea el RemoteExecutionContext y extrae el contact desde
    ///      InputParameters.OpportunityCustomerId o, en su defecto, desde
    ///      OutputParameters.CreatedEntities (flujo estandar de la UI).
    ///   2. Si hay contact, lo lee de Dataverse y lo inserta en CustomersV3 de F&O
    ///      segun el mapeo CustomersV3_Contact.json.
    ///   3. Escribe el CustomerAccount generado en msdyn_contactpersonid del contact.
    ///
    /// Manejo de mensajes (autoComplete = false en host.json):
    ///   - Parse invalido o error de datos no reintentables -> DLQ con motivo.
    ///   - QualifyLead sin contact (ej. customer es un account) -> se completa sin procesar.
    ///   - Errores transitorios (F&O/Dataverse caidos, timeouts) -> Abandon para que
    ///     Service Bus reintente; tras Max Delivery Count el mensaje va al DLQ.
    /// </summary>
    public class QualifyLeadCustomerSyncFunction
    {
        private readonly IContactCustomerSyncService _syncService;
        private readonly ILogger<QualifyLeadCustomerSyncFunction> _logger;

        public QualifyLeadCustomerSyncFunction(
            IContactCustomerSyncService syncService,
            ILogger<QualifyLeadCustomerSyncFunction> logger)
        {
            _syncService = syncService;
            _logger      = logger;
        }

        [Function(nameof(QualifyLeadCustomerSyncFunction))]
        public async Task Run(
            [ServiceBusTrigger(
                "%ServiceBusQueueName%",
                Connection        = "ServiceBusConnection",
                IsSessionsEnabled = false)]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions  messageActions,
            CancellationToken         cancellationToken)
        {
           var messageId     = message.MessageId;
            var deliveryCount = message.DeliveryCount;

            _logger.LogInformation(
                "[QualifyLeadCustomerSyncFunction] Mensaje recibido. MessageId={MessageId} | " +
                "DeliveryCount={DeliveryCount} | EnqueuedAt={EnqueuedAt}",
                messageId, deliveryCount, message.EnqueuedTime);

            QualifyLeadContext context;
            try
            {
                context = QualifyLeadContextParser.Parse(message.Body.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[QualifyLeadCustomerSyncFunction] No se pudo parsear el RemoteExecutionContext " +
                    "{MessageId}. Enviando a DLQ.", messageId);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "ParseFailed",
                    deadLetterErrorDescription:
                        $"El cuerpo del mensaje no es un RemoteExecutionContext valido: {ex.Message}");
                return;
            }

            _logger.LogInformation(
                "[QualifyLeadCustomerSyncFunction] Contexto parseado. MessageId={MessageId} | " +
                "MessageName={MessageName} | LeadId={LeadId} | ContactId={ContactId} | " +
                "OpportunityCustomerId presente={HasOppCustomerId} (LogicalName={CustomerLogicalName}) | " +
                "CreatedEntities presente={HasCreatedEntities} [{CreatedEntityLogicalNames}]",
                messageId,
                context.MessageName,
                context.LeadId?.ToString() ?? "null",
                context.ContactId?.ToString() ?? "null",
                context.HasOpportunityCustomerId,
                context.CustomerLogicalName ?? "null",
                context.HasCreatedEntities,
                string.Join(", ", context.CreatedEntityLogicalNames));

            if (context.ContactId == null)
            {
                // QualifyLead sin contact: no vino en OpportunityCustomerId ni se creo
                // uno al calificar (CreatedEntities). No es un error: se completa sin procesar.
                var body        = message.Body.ToString();
                var bodyPreview = body.Length > 2000 ? body[..2000] + "...(truncado)" : body;

                _logger.LogWarning(
                    "[QualifyLeadCustomerSyncFunction] Mensaje {MessageId} SIN CONTACT: no vino en " +
                    "OpportunityCustomerId (LogicalName={LogicalName}) ni en CreatedEntities " +
                    "[{CreatedEntityLogicalNames}]. Se completa SIN insertar en F&O. " +
                    "Body (preview): {BodyPreview}",
                    messageId,
                    context.CustomerLogicalName ?? "null",
                    string.Join(", ", context.CreatedEntityLogicalNames),
                    bodyPreview);

                await messageActions.CompleteMessageAsync(message);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "[QualifyLeadCustomerSyncFunction] Procesando contact {ContactId} " +
                    "(Lead={LeadId}, Message={MessageName}).",
                    context.ContactId, context.LeadId, context.MessageName);

                await _syncService.ProcessAsync(context.ContactId.Value, cancellationToken);

                await messageActions.CompleteMessageAsync(message);

                _logger.LogInformation(
                    "[QualifyLeadCustomerSyncFunction] Mensaje {MessageId} procesado correctamente.",
                    messageId);
            }
            catch (NonRetryableSyncException ex)
            {
                _logger.LogError(ex,
                    "[QualifyLeadCustomerSyncFunction] Error de datos no reintentable en " +
                    "mensaje {MessageId}: {Error}. Enviando a DLQ.",
                    messageId, ex.Message);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "DataError",
                    deadLetterErrorDescription: ex.Message);
            }
            catch (FoODataException ex) when (ex.IsPermanent)
            {
                // F&O responde 400 ante violaciones de reglas de negocio (customer group
                // inexistente en la compania, party que ya existe como prospect).
                // Reintentar no cambia nada: solo consume delivery count y martilla F&O.
                _logger.LogError(ex,
                    "[QualifyLeadCustomerSyncFunction] F&O rechazo el mensaje {MessageId} " +
                    "con HTTP {Status}. Enviando a DLQ sin reintentar. F&O dijo: {FoMessage}",
                    messageId, (int)ex.Status, ex.Detail);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: EipDeadLetterReason.BusinessRuleFailed,
                    deadLetterErrorDescription: ex.Detail);
            }
            catch (FoODataException ex) when (ex.Status == HttpStatusCode.TooManyRequests)
            {
                // Se agoto el backoff del resilience handler y F&O sigue throttleando.
                // No es un problema del mensaje: se devuelve a la cola. Se loguea aparte
                // porque la causa (saturacion del entorno F&O) y la accion correctiva no
                // tienen nada que ver con las de un error de proceso.
                _logger.LogWarning(ex,
                    "[QualifyLeadCustomerSyncFunction] F&O sigue throttleando (429) despues de " +
                    "agotar los reintentos. Mensaje {MessageId} vuelve a la cola " +
                    "(DeliveryCount={DeliveryCount}). Si se repite, revisar la utilizacion de " +
                    "recursos del entorno F&O. F&O dijo: {FoMessage}",
                    messageId, deliveryCount, ex.Detail);

                await messageActions.AbandonMessageAsync(message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[QualifyLeadCustomerSyncFunction] Error procesando mensaje {MessageId} " +
                    "(DeliveryCount={DeliveryCount}): {Error}",
                    messageId, deliveryCount, ex.Message);

                // Abandonar para que Service Bus reintente
                await messageActions.AbandonMessageAsync(message);

                // Re-lanzar para que Application Insights registre la excepcion
                throw;
            }
        }
    }
}
