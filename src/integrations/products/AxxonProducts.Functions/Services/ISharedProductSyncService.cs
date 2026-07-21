using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    public interface ISharedProductSyncService
    {
        Task SyncAsync(IReadOnlyList<FoReleasedProduct> products, CancellationToken cancellationToken = default);
    }
}
