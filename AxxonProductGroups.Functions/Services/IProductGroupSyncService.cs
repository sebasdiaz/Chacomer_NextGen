using AxxonProductGroups.Functions.Models;

namespace AxxonProductGroups.Functions.Services
{
    public interface IProductGroupSyncService
    {
        Task SyncAsync(IReadOnlyList<FoProductGroup> groups, CancellationToken cancellationToken = default);
    }
}
