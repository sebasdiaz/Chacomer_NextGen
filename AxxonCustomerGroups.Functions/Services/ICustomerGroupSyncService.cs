using AxxonCustomerGroups.Functions.Models;

namespace AxxonCustomerGroups.Functions.Services
{
    public interface ICustomerGroupSyncService
    {
        /// <summary>
        /// Hace upsert de los customer groups de F&O en msdyn_customergroup
        /// usando (msdyn_groupid + msdyn_company) como clave.
        /// </summary>
        Task SyncAsync(
            IReadOnlyList<FoCustomerGroup> groups,
            CancellationToken cancellationToken = default);
    }
}
