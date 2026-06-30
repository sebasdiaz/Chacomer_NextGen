using AxxonProducts.Functions.Models;

namespace AxxonProducts.Functions.Services
{
    public interface IFoProductGroupService
    {
        IAsyncEnumerable<FoProductGroup> GetProductGroupsAsync(CancellationToken cancellationToken = default);
    }
}
