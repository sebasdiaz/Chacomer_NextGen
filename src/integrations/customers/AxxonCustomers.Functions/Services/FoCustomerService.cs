using Axxon.Eip.Core.FinOps;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Inserta y busca customers en F&amp;O via el cliente OData generico de la EiP
    /// (retry de throttling incluido). La compania destino se determina con dataAreaId
    /// en el body del POST.
    /// </summary>
    public class FoCustomerService : IFoCustomerService
    {
        private readonly IFoODataClient _client;
        private readonly ILogger<FoCustomerService> _logger;

        public FoCustomerService(IFoODataClient client, ILogger<FoCustomerService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<FoCustomerV3CreatedResponse> CreateCustomerAsync(
            string entitySet,
            FoPayload payload,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[FoCustomerService] Insertando en {EntitySet}. DataAreaId={DataAreaId} | Campos={FieldCount}",
                entitySet, payload.DataAreaId, payload.Fields.Count);

            var created = await _client.CreateAsync<IDictionary<string, object?>, FoCustomerV3CreatedResponse>(
                entitySet, payload.Fields, cancellationToken);

            _logger.LogInformation(
                "[FoCustomerService] Registro creado en F&O. CustomerAccount={CustomerAccount} | DataAreaId={DataAreaId}",
                created.CustomerAccount, created.DataAreaId);

            return created;
        }

        public async Task<FoCustomerV3CreatedResponse?> FindCustomerAsync(
            string entitySet,
            string dataAreaId,
            string? partyNumber,
            string? customerAccount,
            CancellationToken cancellationToken = default)
        {
            // PartyNumber identifica a la persona/organizacion (un party tiene a lo sumo
            // un customer por compania); CustomerAccount cubre el write-back previo.
            var criteria = new List<string>();
            if (!string.IsNullOrWhiteSpace(partyNumber))
                criteria.Add($"PartyNumber eq '{FoOData.EscapeLiteral(partyNumber)}'");
            if (!string.IsNullOrWhiteSpace(customerAccount))
                criteria.Add($"CustomerAccount eq '{FoOData.EscapeLiteral(customerAccount)}'");

            if (criteria.Count == 0)
            {
                _logger.LogInformation(
                    "[FoCustomerService] FindCustomer sin criterios (PartyNumber y CustomerAccount " +
                    "vacios). Se asume que el customer NO existe en F&O (DataAreaId={DataAreaId}).",
                    dataAreaId);
                return null;
            }

            var filter = $"dataAreaId eq '{FoOData.EscapeLiteral(dataAreaId)}' " +
                         $"and ({string.Join(" or ", criteria)})";

            _logger.LogInformation(
                "[FoCustomerService] GET FindCustomer en {EntitySet}. Filter: {Filter}", entitySet, filter);

            var found = await _client.FindFirstAsync<FoCustomerV3CreatedResponse>(
                new FoODataQuery(entitySet)
                {
                    Filter = filter,
                    Select = "CustomerAccount,PartyNumber,dataAreaId"
                },
                cancellationToken);

            if (found != null)
                _logger.LogInformation(
                    "[FoCustomerService] FindCustomer ENCONTRO un customer existente. " +
                    "CustomerAccount={CustomerAccount} | PartyNumber={PartyNumber} | DataAreaId={DataAreaId}",
                    found.CustomerAccount, found.PartyNumber, dataAreaId);
            else
                _logger.LogInformation(
                    "[FoCustomerService] FindCustomer no encontro coincidencias en {DataAreaId}.",
                    dataAreaId);

            return found;
        }
    }
}
