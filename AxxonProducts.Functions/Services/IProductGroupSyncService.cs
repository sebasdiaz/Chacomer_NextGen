using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    public interface IProductGroupSyncService
    {
        Task SyncAsync(IReadOnlyList<FoProductGroup> groups, CancellationToken cancellationToken = default);
    }
}
