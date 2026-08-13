using AxxonThinkchat.Functions.Models;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>Sincronizacion de templates de Thinkchat hacia axx_metatemplates.</summary>
    public interface ITemplateSyncService
    {
        /// <summary>
        /// Upsertea los templates recibidos y desactiva los registros activos de
        /// Dataverse cuyo axx_id no vino en esta corrida.
        /// </summary>
        Task SyncAsync(IReadOnlyList<ThinkchatTemplate> templates, CancellationToken cancellationToken = default);
    }
}
