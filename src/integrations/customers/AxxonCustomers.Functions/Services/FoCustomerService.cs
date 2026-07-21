using Axxon.Eip.Core.FinOps;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Inserta y busca customers en la entidad CustomersV3 de F&amp;O via el
    /// cliente OData generico de la EiP (retry de throttling incluido).
    /// La compania destino se determina con dataAreaId en el body del POST.
    /// </summary>
    public class FoCustomerService : IFoCustomerService
    {
        private const string EntitySet = "CustomersV3";

        private readonly IFoODataClient _client;
        private readonly ILogger<FoCustomerService> _logger;

        public FoCustomerService(IFoODataClient client, ILogger<FoCustomerService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<FoCustomerV3CreatedResponse> CreateCustomerAsync(
            FoCustomerV3 customer,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[FoCustomerService] Insertando customer en F&O. DataAreaId={DataAreaId} | Identification={Identification}",
                customer.DataAreaId, customer.IdentificationNumber);

            var created = await _client.CreateAsync<FoCustomerV3, FoCustomerV3CreatedResponse>(
                EntitySet, customer, cancellationToken);

            _logger.LogInformation(
                "[FoCustomerService] Customer creado en F&O. CustomerAccount={CustomerAccount} | DataAreaId={DataAreaId}",
                created.CustomerAccount, created.DataAreaId);

            return created;
        }

        public async Task<FoCustomerV3CreatedResponse?> FindCustomerAsync(
            string dataAreaId,
            string? partyNumber,
            string? customerAccount,
            CancellationToken cancellationToken = default)
        {
            // PartyNumber identifica a la persona (un party tiene a lo sumo un
            // customer por compania); CustomerAccount cubre el write-back previo.
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
                "[FoCustomerService] GET FindCustomer. Filter: {Filter}", filter);

            var found = await _client.FindFirstAsync<FoCustomerV3CreatedResponse>(
                new FoODataQuery(EntitySet)
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
