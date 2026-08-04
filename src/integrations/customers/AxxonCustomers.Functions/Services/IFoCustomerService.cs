using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Acceso a la entidad de customers de F&amp;O. El entity set y los campos salen del
    /// <see cref="EntityMap"/>, no estan hardcodeados.
    /// </summary>
    public interface IFoCustomerService
    {
        /// <summary>
        /// Inserta un registro en el entity set indicado.
        /// Devuelve el registro creado (incluye el CustomerAccount generado).
        /// </summary>
        Task<FoCustomerV3CreatedResponse> CreateCustomerAsync(
            string entitySet,
            FoPayload payload,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Actualiza (PATCH) un customer existente. Solo viajan los campos de
        /// <see cref="FoPayload.UpdateFields"/>: la clave (dataAreaId + CustomerAccount)
        /// va en la URL.
        /// </summary>
        Task UpdateCustomerAsync(
            string entitySet,
            string customerAccount,
            FoPayload payload,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Busca un customer existente dentro de la compania indicada, por PartyNumber
        /// y/o CustomerAccount (el que tenga valor).
        /// Devuelve null si no existe o si no se paso ningun criterio.
        /// </summary>
        Task<FoCustomerV3CreatedResponse?> FindCustomerAsync(
            string entitySet,
            string dataAreaId,
            string? partyNumber,
            string? customerAccount,
            CancellationToken cancellationToken = default);
    }
}
