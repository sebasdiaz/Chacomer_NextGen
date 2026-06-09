using AxxonContacts.Functions.Models;

namespace AxxonContacts.Functions.Services
{
    public interface ISharedProductSyncService
    {
        /// <summary>
        /// Sincroniza un batch de productos liberados de F&O hacia msdyn_sharedproductdetails en Dataverse.
        /// Realiza upsert por clave primaria compuesta: msdyn_itemnumber + msdyn_companyid.
        /// </summary>
        Task SyncAsync(IReadOnlyList<FoReleasedProduct> products, CancellationToken cancellationToken = default);
    }
}
