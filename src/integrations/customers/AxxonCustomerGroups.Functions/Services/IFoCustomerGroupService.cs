using AxxonCustomerGroups.Functions.Models;

namespace AxxonCustomerGroups.Functions.Services
{
    public interface IFoCustomerGroupService
    {
        /// <summary>
        /// Lee todos los registros de CustomerGroups de F&O (cross-company)
        /// con paginacion automatica via @odata.nextLink.
        /// </summary>
        IAsyncEnumerable<FoCustomerGroup> GetCustomerGroupsAsync(
            CancellationToken cancellationToken = default);
    }
}
