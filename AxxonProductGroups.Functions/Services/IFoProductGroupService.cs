using AxxonProductGroups.Functions.Models;

namespace AxxonProductGroups.Functions.Services
{
    public interface IFoProductGroupService
    {
        IAsyncEnumerable<FoProductGroup> GetProductGroupsAsync(CancellationToken cancellationToken = default);
    }
}
