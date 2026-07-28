using System.Text.Json;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Orquesta la sincronizacion contact (Dataverse) -&gt; CustomersV3 (F&amp;O):
    ///   1. Recupera el contact con las columnas que pide el mapeo.
    ///   2. Evalua la guarda de sincronizacion del overlay (syncWhen).
    ///   3. Arma el payload con <see cref="FoPayloadBuilder"/> (mapeo por JSON).
    ///   4. Idempotencia: se consulta F&amp;O por PartyNumber / CustomerAccount dentro de
    ///      la compania. El campo de write-back de CRM por si solo no determina la
    ///      existencia: puede traer un valor que no corresponde a un customer real.
    ///   5. Inserta en F&amp;O y escribe el CustomerAccount generado de vuelta en CRM.
    ///
    /// El mapeo de campos NO vive aca: sale de Mappings/customersv3.contact.*.json.
    /// </summary>
    public class ContactCustomerSyncService : IContactCustomerSyncService
    {
        private const string MapName = "contact";

        private static readonly JsonSerializerOptions PayloadLogOptions = new() { WriteIndented = false };

        private readonly IOrganizationService _orgService;
        private readonly IFoCustomerService _foCustomerService;
        private readonly FoPayloadBuilder _payloadBuilder;
        private readonly EntityMap _map;
        private readonly ILogger<ContactCustomerSyncService> _logger;

        public ContactCustomerSyncService(
            IOrganizationService orgService,
            IFoCustomerService foCustomerService,
            FoPayloadBuilder payloadBuilder,
            EntityMapRegistry maps,
            ILogger<ContactCustomerSyncService> logger)
        {
            _orgService        = orgService        ?? throw new ArgumentNullException(nameof(orgService));
            _foCustomerService = foCustomerService ?? throw new ArgumentNullException(nameof(foCustomerService));
            _payloadBuilder    = payloadBuilder    ?? throw new ArgumentNullException(nameof(payloadBuilder));
            _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
            _map               = (maps ?? throw new ArgumentNullException(nameof(maps))).Get(MapName);
        }

        public async Task ProcessAsync(Guid contactId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[ContactCustomerSyncService] Inicio. ContactId={ContactId}", contactId);

            var contact = RetrieveContact(contactId);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Contact recuperado de Dataverse. ContactId={ContactId} | " +
                "Atributos con valor: [{Attributes}]",
                contactId, string.Join(", ", contact.Attributes.Keys));

            if (!_payloadBuilder.ShouldSync(contact, _map, out var reason))
            {
                _logger.LogInformation(
                    "[ContactCustomerSyncService] Contact {ContactId} no cumple la guarda de " +
                    "sincronizacion ({Reason}). No se sincroniza.",
                    contactId, reason);
                return;
            }

            var payload = await _payloadBuilder.BuildAsync(contact, _map, cancellationToken);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Payload {EntitySet} armado para {ContactId}: {Payload}",
                _map.EntitySet, contactId,
                JsonSerializer.Serialize(payload.Fields, PayloadLogOptions));

            // Idempotencia: la existencia se verifica contra F&O, no contra el campo de
            // CRM. El write-back puede tener valor sin que el customer exista (datos
            // previos, write-back de un registro borrado).
            var writtenBackAccount = contact.GetAttributeValue<string>(_map.WriteBackAttribute);
            var partyNumber        = payload.MatchValues.GetValueOrDefault("PartyNumber");

            _logger.LogInformation(
                "[ContactCustomerSyncService] Chequeo de idempotencia contra F&O. " +
                "DataAreaId={DataAreaId} | PartyNumber={PartyNumber} | {WriteBackField} (CRM)={WrittenBackAccount}",
                payload.DataAreaId, partyNumber ?? "null",
                _map.WriteBackAttribute, writtenBackAccount ?? "null");

            var existing = await _foCustomerService.FindCustomerAsync(
                _map.EntitySet, payload.DataAreaId, partyNumber, writtenBackAccount, cancellationToken);

            if (existing != null)
            {
                _logger.LogWarning(
                    "[ContactCustomerSyncService] Contact {ContactId} YA EXISTE en F&O " +
                    "(CustomerAccount={CustomerAccount}, PartyNumber={PartyNumber}, DataAreaId={DataAreaId}). " +
                    "Se OMITE el insert (idempotencia).",
                    contactId, existing.CustomerAccount, existing.PartyNumber, payload.DataAreaId);

                // Re-sincroniza el campo de CRM si quedo desactualizado.
                if (!string.Equals(existing.CustomerAccount, writtenBackAccount, StringComparison.OrdinalIgnoreCase))
                    WriteBackCustomerAccount(contactId, existing.CustomerAccount);
                return;
            }

            _logger.LogInformation(
                "[ContactCustomerSyncService] No existe customer previo en F&O. " +
                "Se procede al insert en {EntitySet} (DataAreaId={DataAreaId}).",
                _map.EntitySet, payload.DataAreaId);

            var created = await _foCustomerService.CreateCustomerAsync(
                _map.EntitySet, payload, cancellationToken);

            WriteBackCustomerAccount(contactId, created.CustomerAccount);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Fin. ContactId={ContactId} | CustomerAccount={CustomerAccount}",
                contactId, created.CustomerAccount ?? "N/A");
        }

        // ── Lectura de Dataverse ──────────────────────────────────────

        private Entity RetrieveContact(Guid contactId)
        {
            try
            {
                return _orgService.Retrieve(
                    _map.SourceEntity,
                    contactId,
                    FoPayloadBuilder.ColumnsFor(_map));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                when (ex.Detail.ErrorCode == unchecked((int)0x80040217)) // ObjectDoesNotExist
            {
                throw new NonRetryableSyncException(
                    $"El contact {contactId} no existe en Dataverse.");
            }
        }

        // ── Write-back ────────────────────────────────────────────────

        private void WriteBackCustomerAccount(Guid contactId, string? customerAccount)
        {
            if (string.IsNullOrWhiteSpace(customerAccount))
            {
                _logger.LogWarning(
                    "[ContactCustomerSyncService] F&O no devolvio CustomerAccount para el " +
                    "contact {ContactId}. Se omite el write-back (sin idempotencia para reintentos).",
                    contactId);
                return;
            }

            var update = new Entity(_map.SourceEntity, contactId)
            {
                [_map.WriteBackAttribute] = customerAccount
            };

            _orgService.Update(update);

            _logger.LogInformation(
                "[ContactCustomerSyncService] Write-back OK. Contact={ContactId} | " +
                "{WriteBackField}={CustomerAccount}",
                contactId, _map.WriteBackAttribute, customerAccount);
        }
    }
}
