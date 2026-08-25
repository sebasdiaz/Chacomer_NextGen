using Axxon.Eip.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Publica en la cola de LTM el registro cuyo customer acaba de quedar creado en F&amp;O,
    /// para escribir su contraparte de localizacion PY (<c>LTMCustTable</c>).
    ///
    /// Es una llamada explicita y no un efecto secundario del write-back a proposito: colgar
    /// el flujo de que "alguien escriba un campo" lo vuelve invisible para quien lee el
    /// codigo, y lo ata a los filtering attributes de un step de Dataverse (ADR-001).
    ///
    /// El payload es una referencia (<see cref="CustomerSyncPayload"/>): el consumidor relee
    /// Dataverse. El CustomerAccount no viaja en el mensaje justamente por eso — se vuelve a
    /// leer del registro, que es la fuente de verdad.
    ///
    /// <b>Este es el unico disparador de la v1, y solo cubre el alta.</b> Las modificaciones
    /// quedan fuera de alcance: sin PATCH, encolarlas produciria un POST sobre una fila que
    /// ya existe, un 400 y un mensaje en el DLQ por cada cambio de cliente. Cuando se sume la
    /// modificacion hara falta un segundo disparador desde AxxonContacts que —a diferencia de
    /// <c>FoSyncDispatcher</c>— no filtre por Dual Write, porque en las legal entities que si
    /// estan en Dual Write las modificaciones no pasan por nuestras Functions.
    /// </summary>
    public class LtmSyncDispatcher
    {
        private readonly IEipMessagePublisher _publisher;
        private readonly string _queueName;
        private readonly ILogger<LtmSyncDispatcher> _logger;

        public LtmSyncDispatcher(
            IEipMessagePublisher publisher,
            string queueName,
            ILogger<LtmSyncDispatcher> logger)
        {
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
            _logger    = logger    ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task DispatchAsync(
            string entityLogicalName,
            Guid recordId,
            string? dataAreaId,
            CancellationToken cancellationToken = default)
        {
            var envelope = EipMessage<CustomerSyncPayload>.Create(
                source:     EipSource.Dataverse,
                entityType: entityLogicalName,
                operation:  EipOperation.Create,
                payload:    new CustomerSyncPayload
                {
                    RecordId   = recordId,
                    DataAreaId = dataAreaId
                },
                // Misma session que customer-fo-sync: el registro. Dos mensajes del mismo
                // cliente no pueden procesarse fuera de orden.
                partitionKey: recordId.ToString());

            await _publisher.PublishAsync(_queueName, envelope, cancellationToken);

            _logger.LogInformation(
                "[LtmSyncDispatcher] {Entity} {RecordId} ({DataAreaId}) encolado en '{Queue}' para " +
                "crear la fila de LTMCustTable. MessageId={MessageId}",
                entityLogicalName, recordId, dataAreaId ?? "sin resolver", _queueName,
                envelope.MessageId);
        }
    }
}
