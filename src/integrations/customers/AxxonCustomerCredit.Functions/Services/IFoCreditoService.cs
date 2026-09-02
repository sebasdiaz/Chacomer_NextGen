using AxxonCustomerCredit.Functions.Models;

namespace AxxonCustomerCredit.Functions.Services
{
    /// <summary>
    /// Lectura de las cuatro entidades de credito de F&amp;O
    /// (<c>DevAxCustCredit*</c>). Solo lectura: no hay create ni update.
    /// </summary>
    public interface IFoCreditoService
    {
        /// <summary>Ficha crediticia del cliente — <c>DevAxCustCreditCustomers</c>.</summary>
        Task<CreditoResultado<FoCreditoCliente>> GetClientesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default);

        /// <summary>Planes otorgados — <c>DevAxCustCreditGrantedPlans</c>.</summary>
        Task<CreditoResultado<FoCreditoPlan>> GetPlanesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default);

        /// <summary>Cuotas de los planes — <c>DevAxCustCreditInstallments</c>.</summary>
        Task<CreditoResultado<FoCreditoCuota>> GetCuotasAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default);

        /// <summary>Resoluciones de solicitudes — <c>DevAxCustCreditResolutions</c>.</summary>
        Task<CreditoResultado<FoCreditoResolucion>> GetResolucionesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default);
    }
}
