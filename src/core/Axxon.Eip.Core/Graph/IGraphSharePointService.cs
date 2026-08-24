namespace Axxon.Eip.Core.Graph
{
    /// <summary>Item del drive de SharePoint devuelto por Graph.</summary>
    public sealed record GraphDriveItem(string Id, string Name, string WebUrl);

    /// <summary>
    /// Operaciones de SharePoint via Microsoft Graph que la EiP necesita:
    /// subir un archivo al drive del sitio, convertir Office -&gt; PDF y borrar.
    /// </summary>
    public interface IGraphSharePointService
    {
        /// <summary>Resuelve (y cachea) el site id del <c>SharePointSiteUrl</c> configurado.</summary>
        Task<string> GetSiteIdAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sube un archivo al drive por defecto del sitio.
        /// </summary>
        /// <param name="drivePath">
        /// Ruta relativa a la raiz del drive, sin barra inicial. Ej: "Temp/ticket-abc.docx".
        /// </param>
        Task<GraphDriveItem> UploadAsync(
            string drivePath, byte[] content, string contentType, CancellationToken cancellationToken = default);

        /// <summary>Descarga un item convertido a PDF (<c>/content?format=pdf</c>).</summary>
        Task<byte[]> DownloadAsPdfAsync(string itemId, CancellationToken cancellationToken = default);

        /// <summary>Borra un item del drive. No lanza si ya no existe.</summary>
        Task DeleteAsync(string itemId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Crea la carpeta y todas sus intermedias si no existen. Idempotente.
        ///
        /// Hace falta porque el upload simple de Graph (PUT .../root:/{path}:/content) NO
        /// crea los directorios del camino: si falta uno, responde 404 itemNotFound.
        /// </summary>
        /// <param name="folderPath">Ruta relativa a la raiz del drive, sin barra inicial.</param>
        Task EnsureFolderAsync(string folderPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Convierte un documento de Office a PDF: lo sube a una carpeta temporal, pide la
        /// conversion y borra el temporal en un finally.
        /// </summary>
        /// <param name="tempFolder">Carpeta del drive donde dejar el temporal. Ej: "Temp/TicketAtencion".</param>
        Task<byte[]> ConvertToPdfAsync(
            byte[] officeDocument,
            string tempFolder,
            string fileExtension,
            string contentType,
            CancellationToken cancellationToken = default);
    }
}
