using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Decide si un raw (account o contact) tiene que ir a F&amp;O por API y, en ese caso,
    /// lo publica en la cola de fo-sync.
    ///
    /// El criterio es uno solo: <c>cdm_isenabledfordualwrite</c> de la legal entity del
    /// registro. Si esta en Dual Write, Dual Write se encarga y nosotros no publicamos
    /// nada. Si esta explicitamente fuera, lo publicamos. Si no se puede determinar, no
    /// se publica (ver <see cref="DualWriteCompanyResolver"/>: ante la duda no escribimos
    /// en el ERP).
    ///
    /// Filtrar aca y no en el consumidor evita meter en la cola el volumen entero de
    /// accounts y contacts para descartarlo del otro lado.
    /// </summary>
    public class FoSyncDispatcher
    {
        private readonly IEipMessagePublisher _publisher;
        private readonly IDualWriteCompanyResolver _companyResolver;
        private readonly string _queueName;
        private readonly ILogger<FoSyncDispatcher> _logger;

        public FoSyncDispatcher(
            IEipMessagePublisher publisher,
            IDualWriteCompanyResolver companyResolver,
            string queueName,
            ILogger<FoSyncDispatcher> logger)
        {
            _publisher       = publisher       ?? throw new ArgumentNullException(nameof(publisher));
            _companyResolver = companyResolver ?? throw new ArgumentNullException(nameof(companyResolver));
            _queueName       = queueName       ?? throw new ArgumentNullException(nameof(queueName));
            _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task DispatchAsync(
            string entityLogicalName,
            Guid recordId,
            string triggerMessage,
            bool isMaster,
            CancellationToken cancellationToken = default)
        {
            var operation = OperationFrom(triggerMessage);

            if (operation is null)
            {
                _logger.LogInformation(
                    "[FoSyncDispatcher] Evento '{Trigger}' de {Entity} {RecordId}: no genera " +
                    "sincronizacion hacia F&O.",
                    triggerMessage, entityLogicalName, recordId);
                return;
            }

            // El master es un registro nuestro de consolidacion, no un cliente de una
            // compania: nunca va a F&O. Por eso el contact master se crea sin
            // msdyn_company y con msdyn_sellable = false, y el account master con
            // customertypecode = 12 (MasterFoIsolationPlugin sostiene esos valores).
            if (isMaster)
            {
                _logger.LogInformation(
                    "[FoSyncDispatcher] {Entity} {RecordId} es Master: no se sincroniza a F&O.",
                    entityLogicalName, recordId);
                return;
            }

            var company = await _companyResolver.ResolveForRecordAsync(
                entityLogicalName, recordId, cancellationToken: cancellationToken);

            if (company.Handling != CompanySyncHandling.Api)
            {
                _logger.LogInformation(
                    "[FoSyncDispatcher] {Entity} {RecordId} pertenece a la legal entity " +
                    "{DataAreaId} ({Handling}): no se publica en '{Queue}'.",
                    entityLogicalName, recordId, company.DataAreaId ?? "sin codigo",
                    company.Handling, _queueName);
                return;
            }

            var envelope = EipMessage<CustomerSyncPayload>.Create(
                source:     EipSource.Dataverse,
                entityType: entityLogicalName,
                operation:  operation,
                payload:    new CustomerSyncPayload
                {
                    RecordId   = recordId,
                    DataAreaId = company.DataAreaId
                },
                // La session es el registro: dos modificaciones del mismo account no
                // pueden procesarse fuera de orden y dejar F&O con el valor viejo.
                partitionKey: recordId.ToString());

            await _publisher.PublishAsync(_queueName, envelope, cancellationToken);

            _logger.LogInformation(
                "[FoSyncDispatcher] {Entity} {RecordId} ({DataAreaId}) encolado en '{Queue}' " +
                "para sincronizar a F&O. Operacion={Operation} | MessageId={MessageId}",
                entityLogicalName, recordId, company.DataAreaId, _queueName,
                operation, envelope.MessageId);
        }

        private static string? OperationFrom(string triggerMessage) =>
            triggerMessage switch
            {
                _ when string.Equals(triggerMessage, "Create", StringComparison.OrdinalIgnoreCase)
                    => EipOperation.Create,
                _ when string.Equals(triggerMessage, "Update", StringComparison.OrdinalIgnoreCase)
                    => EipOperation.Update,
                _ => null
            };
    }
}
