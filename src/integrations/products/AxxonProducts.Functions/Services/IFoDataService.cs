using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    public interface IFoDataService
    {
        IAsyncEnumerable<FoReleasedProduct> GetReleasedProductsAsync(CancellationToken cancellationToken = default);
    }
}
