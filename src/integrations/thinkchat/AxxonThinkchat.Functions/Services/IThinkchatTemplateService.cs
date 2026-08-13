using AxxonThinkchat.Functions.Models;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>Lectura de templates desde la API de Thinkchat.</summary>
    public interface IThinkchatTemplateService
    {
        /// <summary>
        /// Trae todos los templates. Devuelve la lista completa materializada:
        /// el sync necesita el universo entero para saber cuales desactivar.
        /// </summary>
        Task<IReadOnlyList<ThinkchatTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    }
}
