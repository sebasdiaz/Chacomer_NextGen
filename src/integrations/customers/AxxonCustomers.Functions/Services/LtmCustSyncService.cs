using System.Text.Json;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Services
{
    public interface ILtmCustSyncService
    {
        /// <summary>
        /// Sincroniza la contraparte de localizacion del cliente hacia F&amp;O.
        /// Devuelve <c>false</c> si el registro todavia no se puede sincronizar (ver la
        /// guarda del <c>AccountNum</c>), sin que eso sea un error.
        /// </summary>
        Task<bool> ProcessAsync(
            string entityLogicalName,
            Guid recordId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Orquesta la sincronizacion de un contact o account de Dataverse hacia
    /// <c>LTMCustTable</c>:
    ///   1. Recupera el registro con las columnas que pide el mapeo.
    ///   2. Evalua la guarda del <c>AccountNum</c>.
    ///   3. Arma el payload con <see cref="LtmCustPayloadBuilder"/>.
    ///   4. Hace el POST contra F&amp;O.
    ///
    /// Igual que <see cref="CustomerSyncService"/>, relee Dataverse en vez de mapear desde
    /// el mensaje: el payload de la cola es una referencia, no un snapshot (decision #2).
    /// </summary>
    public class LtmCustSyncService : ILtmCustSyncService
    {
        private static readonly JsonSerializerOptions PayloadLogOptions = new() { WriteIndented = false };

        private readonly IOrganizationService _orgService;
        private readonly LtmCustPayloadBuilder _payloadBuilder;
        private readonly ILtmCustService _ltmCustService;
        private readonly ILogger<LtmCustSyncService> _logger;

        public LtmCustSyncService(
            IOrganizationService orgService,
            LtmCustPayloadBuilder payloadBuilder,
            ILtmCustService ltmCustService,
            ILogger<LtmCustSyncService> logger)
        {
            _orgService     = orgService     ?? throw new ArgumentNullException(nameof(orgService));
            _payloadBuilder = payloadBuilder ?? throw new ArgumentNullException(nameof(payloadBuilder));
            _ltmCustService = ltmCustService ?? throw new ArgumentNullException(nameof(ltmCustService));
            _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ProcessAsync(
            string entityLogicalName,
            Guid recordId,
            CancellationToken cancellationToken = default)
        {
            var source = LtmCustSource.For(entityLogicalName)
                ?? throw new NonRetryableSyncException(
                    $"'{entityLogicalName}' no es una entidad valida para LTMCustTable " +
                    "(se esperaba 'contact' o 'account').");

            _logger.LogInformation(
                "[LtmCustSyncService] Inicio. {Entity}={RecordId}", source.EntityLogicalName, recordId);

            var record = RetrieveRecord(source, recordId);

            // Guarda: sin CustomerAccount no hay clave para la fila de LTMCustTable.
            //
            // No es un error ni un dato roto: es el orden natural del alta. El write-back
            // llega recien cuando CustomerSyncService creo el customer en F&O, y ese mismo
            // write-back vuelve a encolar el registro aca. Por eso se completa el mensaje
            // sin procesar en vez de reintentar o mandarlo al DLQ.
            var accountNum = record.GetAttributeValue<string>(source.AccountNumberAttribute);

            if (string.IsNullOrWhiteSpace(accountNum))
            {
                _logger.LogInformation(
                    "[LtmCustSyncService] El {Entity} {RecordId} todavia no tiene " +
                    "{Attribute}: el customer aun no existe en F&O. No se sincroniza " +
                    "LTMCustTable (se reintenta cuando llegue el write-back).",
                    source.EntityLogicalName, recordId, source.AccountNumberAttribute);
                return false;
            }

            var payload = await _payloadBuilder.BuildAsync(record, source, accountNum, cancellationToken);

            _logger.LogInformation(
                "[LtmCustSyncService] Payload {EntitySet} armado para {Entity} {RecordId}: {Payload}",
                LtmCustMapping.EntitySet, source.EntityLogicalName, recordId,
                JsonSerializer.Serialize(payload.Fields, PayloadLogOptions));

            await _ltmCustService.CreateAsync(payload, cancellationToken);

            _logger.LogInformation(
                "[LtmCustSyncService] Fin. {Entity}={RecordId} | AccountNum={AccountNum} | " +
                "DataAreaId={DataAreaId}",
                source.EntityLogicalName, recordId, payload.AccountNum, payload.DataAreaId);

            return true;
        }

        private Entity RetrieveRecord(LtmCustSource source, Guid recordId)
        {
            try
            {
                return _orgService.Retrieve(
                    source.EntityLogicalName,
                    recordId,
                    LtmCustPayloadBuilder.ColumnsFor(source));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                when (ex.Detail.ErrorCode == unchecked((int)0x80040217)) // ObjectDoesNotExist
            {
                throw new NonRetryableSyncException(
                    $"El {source.EntityLogicalName} {recordId} no existe en Dataverse.");
            }
        }
    }
}
