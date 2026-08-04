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
    /// Azure Function disparada por el Service Endpoint de Dataverse (mensaje QualifyLead)
    /// via la cola "leadcontacts" del Service Bus.
    ///
    /// Flujo:
    ///   1. Parsea el RemoteExecutionContext y extrae el contact desde
    ///      InputParameters.OpportunityCustomerId o, en su defecto, desde
    ///      OutputParameters.CreatedEntities (flujo estandar de la UI).
    ///   2. Si hay contact, lo lee de Dataverse y lo inserta en CustomersV3 de F&O
    ///      segun el mapeo customersv3.contact.*.json.
    ///   3. Escribe el CustomerAccount generado en msdyn_contactpersonid del contact.
    ///
    /// Alcance: legal entities que SI sincroniza Dual Write. Dual Write en direccion
    /// CRM -> AX solo actualiza customers existentes (su filtro pide
    /// msdyn_contactpersonid ne null), nunca crea: este flujo cubre justamente ese alta.
    /// Las legal entities que NO estan en Dual Write las toma CustomerFoSyncFunction de
    /// punta a punta, y aca se saltean.
    ///
    /// Manejo de mensajes (autoComplete = false en host.json):
    ///   - Parse invalido o error de datos no reintentables -> DLQ con motivo.
    ///   - QualifyLead sin contact (ej. customer es un account) -> se completa sin procesar.
    ///   - Contact de una legal entity fuera de Dual Write -> se completa sin procesar.
    ///   - Errores transitorios (F&O/Dataverse caidos, timeouts) -> Abandon para que
    ///     Service Bus reintente; tras Max Delivery Count el mensaje va al DLQ.
    /// </summary>
    public class QualifyLeadCustomerSyncFunction
    {
        private const string MapName = "contact";

        private readonly ICustomerSyncService _syncService;
        private readonly IDualWriteCompanyResolver _companyResolver;
        private readonly ILogger<QualifyLeadCustomerSyncFunction> _logger;

        public QualifyLeadCustomerSyncFunction(
            ICustomerSyncService syncService,
            IDualWriteCompanyResolver companyResolver,
            ILogger<QualifyLeadCustomerSyncFunction> logger)
        {
            _syncService     = syncService;
            _companyResolver = companyResolver;
            _logger          = logger;
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

            // Reparto excluyente con CustomerFoSyncFunction: las legal entities que NO
            // estan en Dual Write las sincroniza fo-sync de punta a punta (alta y
            // modificacion). Si las procesaramos las dos, el alta saldria por duplicado y
            // F&O rechazaria la segunda con un 400.
            //
            // Solo se saltea cuando la company dice explicitamente que no esta en Dual
            // Write. Indeterminada (flag sin setear) sigue el camino de siempre: mientras
            // cdm_isenabledfordualwrite no este poblado, este flujo se comporta como antes.
            var company = await _companyResolver.ResolveForRecordAsync(
                MapName, context.ContactId.Value, cancellationToken: cancellationToken);

            if (company.Handling == CompanySyncHandling.Api)
            {
                _logger.LogInformation(
                    "[QualifyLeadCustomerSyncFunction] El contact {ContactId} es de la legal entity " +
                    "{DataAreaId}, que no esta en Dual Write: lo sincroniza CustomerFoSyncFunction. " +
                    "Se completa sin procesar.",
                    context.ContactId, company.DataAreaId ?? "sin codigo");

                await messageActions.CompleteMessageAsync(message);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "[QualifyLeadCustomerSyncFunction] Procesando contact {ContactId} " +
                    "(Lead={LeadId}, Message={MessageName}, legal entity={DataAreaId}/{Handling}).",
                    context.ContactId, context.LeadId, context.MessageName,
                    company.DataAreaId ?? "sin codigo", company.Handling);

                await _syncService.ProcessAsync(MapName, context.ContactId.Value, cancellationToken);

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
