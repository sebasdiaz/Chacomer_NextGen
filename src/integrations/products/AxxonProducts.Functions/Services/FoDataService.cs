using Axxon.Eip.Core.FinOps;
using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    /// <summary>
    /// Lee ReleasedProductsV2 de F&amp;O via el cliente OData generico de la EiP
    /// (cross-company, paginacion con @odata.nextLink y retry de throttling).
    /// </summary>
    public class FoDataService : IFoDataService
    {
        private const string EntitySet = "ReleasedProductsV2";

        private readonly IFoODataClient _client;

        public FoDataService(IFoODataClient client) => _client = client;

        public IAsyncEnumerable<FoReleasedProduct> GetReleasedProductsAsync(
            CancellationToken cancellationToken = default)
            => _client.QueryAsync<FoReleasedProduct>(new FoODataQuery(EntitySet), cancellationToken);
    }
}
