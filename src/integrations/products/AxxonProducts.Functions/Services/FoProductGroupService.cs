using Axxon.Eip.Core.FinOps;
using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    /// <summary>
    /// Lee ProductGroups de F&amp;O via el cliente OData generico de la EiP
    /// (cross-company, paginacion con @odata.nextLink y retry de throttling).
    /// </summary>
    public class FoProductGroupService : IFoProductGroupService
    {
        private const string EntitySet = "ProductGroups";

        private readonly IFoODataClient _client;

        public FoProductGroupService(IFoODataClient client) => _client = client;

        public IAsyncEnumerable<FoProductGroup> GetProductGroupsAsync(
            CancellationToken cancellationToken = default)
            => _client.QueryAsync<FoProductGroup>(new FoODataQuery(EntitySet), cancellationToken);
    }
}
