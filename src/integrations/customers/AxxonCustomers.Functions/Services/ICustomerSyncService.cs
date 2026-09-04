using Axxon.Eip.Core.Dataverse;

namespace AxxonCustomers.Functions.Services
{
    public interface ICustomerSyncService
    {
        /// <summary>
        /// Lee el registro de Dataverse, lo mapea segun el <c>EntityMap</c> indicado y lo
        /// crea o actualiza en la entidad de customers de F&amp;O. El CustomerAccount
        /// generado por F&amp;O vuelve al campo de write-back del mapeo.
        /// </summary>
        /// <param name="mapName">Nombre del mapeo: "contact" o "account".</param>
        /// <param name="recordId">Id del registro de Dataverse.</param>
        /// <param name="handling">
        /// Como se sincroniza la legal entity del registro. Decide si aplica la guarda
        /// <c>syncWhen</c> del overlay: fuera de Dual Write no aplica, porque ahi esta
        /// Function es el unico camino al ERP. Ver <c>FoPayloadBuilder.ShouldSync</c>.
        /// </param>
        Task ProcessAsync(
            string mapName,
            Guid recordId,
            CompanySyncHandling handling,
            CancellationToken cancellationToken = default);
    }
}
