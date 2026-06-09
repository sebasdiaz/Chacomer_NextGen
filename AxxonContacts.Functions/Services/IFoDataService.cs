using AxxonContacts.Functions.Models;

namespace AxxonContacts.Functions.Services
{
    public interface IFoDataService
    {
        /// <summary>
        /// Lee todos los productos liberados de F&O usando paginacion OData.
        /// Usa cross-company=true para obtener registros de todas las companias.
        /// </summary>
        IAsyncEnumerable<FoReleasedProduct> GetReleasedProductsAsync(CancellationToken cancellationToken = default);
    }
}
