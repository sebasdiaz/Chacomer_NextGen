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
    }
}
