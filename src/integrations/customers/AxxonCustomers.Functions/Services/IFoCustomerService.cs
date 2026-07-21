using AxxonCustomers.Functions.Models;

namespace AxxonCustomers.Functions.Services
{
    public interface IFoCustomerService
    {
        /// <summary>
        /// Inserta un customer en la entidad CustomersV3 de F&O.
        /// Devuelve el registro creado (incluye el CustomerAccount generado).
        /// </summary>
        Task<FoCustomerV3CreatedResponse> CreateCustomerAsync(
            FoCustomerV3 customer,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Busca un customer existente en CustomersV3 dentro de la compania
        /// indicada, por PartyNumber y/o CustomerAccount (el que tenga valor).
        /// Devuelve null si no existe o si no se paso ningun criterio.
        /// </summary>
        Task<FoCustomerV3CreatedResponse?> FindCustomerAsync(
            string dataAreaId,
            string? partyNumber,
            string? customerAccount,
            CancellationToken cancellationToken = default);
    }
}
