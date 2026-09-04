using System.Text.Json;
using Axxon.Eip.Core.Dataverse;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Orquesta la sincronizacion de un registro de Dataverse (contact o account) hacia
    /// CustomersV3 (F&amp;O):
    ///   1. Recupera el registro con las columnas que pide el mapeo.
    ///   2. Evalua la guarda de sincronizacion del overlay (syncWhen).
    ///   3. Arma el payload con <see cref="FoPayloadBuilder"/> (mapeo por JSON).
    ///   4. Busca el customer en F&amp;O por PartyNumber / CustomerAccount dentro de la
    ///      compania. El campo de write-back de CRM por si solo no determina la
    ///      existencia: puede traer un valor que no corresponde a un customer real.
    ///   5. Si existe lo actualiza (PATCH); si no, lo inserta y escribe el
    ///      CustomerAccount generado de vuelta en CRM.
    ///
    /// El mapeo de campos NO vive aca: sale de Mappings/customersv3.{mapa}.*.json.
    /// </summary>
    public class CustomerSyncService : ICustomerSyncService
    {
        private const string PartyNumberField = "PartyNumber";

        private static readonly JsonSerializerOptions PayloadLogOptions = new() { WriteIndented = false };

        private readonly IOrganizationService _orgService;
        private readonly IFoCustomerService _foCustomerService;
        private readonly FoPayloadBuilder _payloadBuilder;
        private readonly EntityMapRegistry _maps;
        private readonly LtmSyncDispatcher _ltmDispatcher;
        private readonly ILogger<CustomerSyncService> _logger;

        public CustomerSyncService(
            IOrganizationService orgService,
            IFoCustomerService foCustomerService,
            FoPayloadBuilder payloadBuilder,
            EntityMapRegistry maps,
            LtmSyncDispatcher ltmDispatcher,
            ILogger<CustomerSyncService> logger)
        {
            _orgService        = orgService        ?? throw new ArgumentNullException(nameof(orgService));
            _foCustomerService = foCustomerService ?? throw new ArgumentNullException(nameof(foCustomerService));
            _payloadBuilder    = payloadBuilder    ?? throw new ArgumentNullException(nameof(payloadBuilder));
            _maps              = maps              ?? throw new ArgumentNullException(nameof(maps));
            _ltmDispatcher     = ltmDispatcher     ?? throw new ArgumentNullException(nameof(ltmDispatcher));
            _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAsync(
            string mapName,
            Guid recordId,
            CompanySyncHandling handling,
            CancellationToken cancellationToken = default)
        {
            var map = _maps.Get(mapName);

            _logger.LogInformation(
                "[CustomerSyncService] Inicio. Mapeo={Map} | {Entity}={RecordId}",
                map.Name, map.SourceEntity, recordId);

            var record = RetrieveRecord(map, recordId);

            _logger.LogInformation(
                "[CustomerSyncService] {Entity} {RecordId} recuperado de Dataverse. " +
                "Atributos con valor: [{Attributes}]",
                map.SourceEntity, recordId, string.Join(", ", record.Attributes.Keys));

            if (!_payloadBuilder.ShouldSync(record, map, handling, out var reason))
            {
                _logger.LogInformation(
                    "[CustomerSyncService] {Entity} {RecordId} no cumple la guarda de " +
                    "sincronizacion ({Reason}). No se sincroniza. (Legal entity={Handling}: " +
                    "fuera de Dual Write la guarda no se evalua.)",
                    map.SourceEntity, recordId, reason, handling);
                return;
            }

            var payload = await _payloadBuilder.BuildAsync(record, map, cancellationToken);

            _logger.LogInformation(
                "[CustomerSyncService] Payload {EntitySet} armado para {Entity} {RecordId}: {Payload}",
                map.EntitySet, map.SourceEntity, recordId,
                JsonSerializer.Serialize(payload.Fields, PayloadLogOptions));

            // La existencia se verifica contra F&O, no contra el campo de CRM. El
            // write-back puede tener valor sin que el customer exista (datos previos,
            // write-back de un registro borrado).
            var writtenBackAccount = record.GetAttributeValue<string>(map.WriteBackAttribute);
            var partyNumber        = payload.MatchValues.GetValueOrDefault(PartyNumberField);

            _logger.LogInformation(
                "[CustomerSyncService] Buscando el customer en F&O. DataAreaId={DataAreaId} | " +
                "PartyNumber={PartyNumber} | {WriteBackField} (CRM)={WrittenBackAccount}",
                payload.DataAreaId, partyNumber ?? "null",
                map.WriteBackAttribute, writtenBackAccount ?? "null");

            var existing = await _foCustomerService.FindCustomerAsync(
                map.EntitySet, payload.DataAreaId, partyNumber, writtenBackAccount, cancellationToken);

            if (existing is not null)
            {
                await UpdateExistingAsync(
                    map, recordId, existing, payload, writtenBackAccount, cancellationToken);
                return;
            }

            var customerAccount = await CreateNewAsync(map, recordId, payload, cancellationToken);

            // La contraparte de localizacion (LTMCustTable) va por su propia cola: se clavea
            // con el CustomerAccount, que recien existe aca.
            //
            // Solo en el alta. La v1 escribe LTMCustTable con un POST y sin PATCH, asi que
            // encolar tambien la modificacion produciria un POST sobre una fila existente, un
            // 400 y un mensaje en el DLQ por cada cambio de cliente (ADR-001).
            //
            // Si F&O no devolvio CustomerAccount no se encola: el consumidor no tendria clave
            // y completaria el mensaje sin hacer nada.
            if (!string.IsNullOrWhiteSpace(customerAccount))
                await _ltmDispatcher.DispatchAsync(
                    map.SourceEntity, recordId, payload.DataAreaId, cancellationToken);
        }

        // ── Alta ──────────────────────────────────────────────────────

        /// <summary>Devuelve el CustomerAccount que genero F&amp;O.</summary>
        private async Task<string?> CreateNewAsync(
            EntityMap map,
            Guid recordId,
            FoPayload payload,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[CustomerSyncService] No existe customer previo en F&O. " +
                "Se procede al insert en {EntitySet} (DataAreaId={DataAreaId}).",
                map.EntitySet, payload.DataAreaId);

            var created = await _foCustomerService.CreateCustomerAsync(
                map.EntitySet, payload, cancellationToken);

            WriteBackCustomerAccount(map, recordId, created.CustomerAccount);

            _logger.LogInformation(
                "[CustomerSyncService] Fin (alta). {Entity}={RecordId} | CustomerAccount={CustomerAccount}",
                map.SourceEntity, recordId, created.CustomerAccount ?? "N/A");

            return created.CustomerAccount;
        }

        // ── Modificacion ──────────────────────────────────────────────

        private async Task UpdateExistingAsync(
            EntityMap map,
            Guid recordId,
            FoCustomerV3CreatedResponse existing,
            FoPayload payload,
            string? writtenBackAccount,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(existing.CustomerAccount))
            {
                // Sin CustomerAccount no hay clave para el PATCH. Es un dato roto de F&O,
                // no algo que un reintento pueda arreglar.
                throw new NonRetryableSyncException(
                    $"F&O devolvio un customer sin CustomerAccount para {map.SourceEntity} " +
                    $"{recordId} (PartyNumber={existing.PartyNumber ?? "null"}, " +
                    $"DataAreaId={payload.DataAreaId}). No se puede actualizar.");
            }

            _logger.LogInformation(
                "[CustomerSyncService] El customer ya existe en F&O " +
                "(CustomerAccount={CustomerAccount}, PartyNumber={PartyNumber}, DataAreaId={DataAreaId}). " +
                "Se actualiza con {FieldCount} campo(s).",
                existing.CustomerAccount, existing.PartyNumber, payload.DataAreaId,
                payload.UpdateFields.Count);

            await _foCustomerService.UpdateCustomerAsync(
                map.EntitySet, existing.CustomerAccount, payload, cancellationToken);

            // Re-sincroniza el campo de CRM si quedo desactualizado.
            if (!string.Equals(existing.CustomerAccount, writtenBackAccount, StringComparison.OrdinalIgnoreCase))
                WriteBackCustomerAccount(map, recordId, existing.CustomerAccount);

            _logger.LogInformation(
                "[CustomerSyncService] Fin (modificacion). {Entity}={RecordId} | CustomerAccount={CustomerAccount}",
                map.SourceEntity, recordId, existing.CustomerAccount);
        }

        // ── Lectura de Dataverse ──────────────────────────────────────

        private Entity RetrieveRecord(EntityMap map, Guid recordId)
        {
            try
            {
                return _orgService.Retrieve(
                    map.SourceEntity,
                    recordId,
                    FoPayloadBuilder.ColumnsFor(map));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                when (ex.Detail.ErrorCode == unchecked((int)0x80040217)) // ObjectDoesNotExist
            {
                throw new NonRetryableSyncException(
                    $"El {map.SourceEntity} {recordId} no existe en Dataverse.");
            }
        }

        // ── Write-back ────────────────────────────────────────────────

        private void WriteBackCustomerAccount(EntityMap map, Guid recordId, string? customerAccount)
        {
            if (string.IsNullOrWhiteSpace(customerAccount))
            {
                _logger.LogWarning(
                    "[CustomerSyncService] F&O no devolvio CustomerAccount para el " +
                    "{Entity} {RecordId}. Se omite el write-back (sin idempotencia para reintentos).",
                    map.SourceEntity, recordId);
                return;
            }

            var update = new Entity(map.SourceEntity, recordId)
            {
                [map.WriteBackAttribute] = customerAccount
            };

            _orgService.Update(update);

            _logger.LogInformation(
                "[CustomerSyncService] Write-back OK. {Entity}={RecordId} | " +
                "{WriteBackField}={CustomerAccount}",
                map.SourceEntity, recordId, map.WriteBackAttribute, customerAccount);
        }
    }
}
