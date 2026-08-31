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
        /// Resuelve (y cachea) el id del drive de una biblioteca de documentos del sitio,
        /// por su nombre.
        ///
        /// Un sitio tiene VARIAS bibliotecas y <c>sites/{id}/drive</c> devuelve solo la de
        /// por defecto ("Documentos compartidos"). Dataverse, en cambio, crea una biblioteca
        /// propia por tabla —<c>msauto_serviceappointment</c>, <c>contact</c>, ...— y sus
        /// carpetas de registro cuelgan de ahi. Subir a la de por defecto deja el archivo en
        /// una carpeta del mismo nombre pero en otra biblioteca: existe, se sube sin error, y
        /// el registro de Dataverse no lo muestra nunca.
        /// </summary>
        Task<string> GetDriveIdAsync(string libraryName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sube un archivo a un drive del sitio.
        /// </summary>
        /// <param name="drivePath">
        /// Ruta relativa a la raiz del drive, sin barra inicial. Ej: "Temp/ticket-abc.docx".
        /// </param>
        /// <param name="driveId">
        /// Drive destino. <c>null</c> = la biblioteca por defecto del sitio.
        /// </param>
        Task<GraphDriveItem> UploadAsync(
            string drivePath, byte[] content, string contentType,
            string? driveId = null, CancellationToken cancellationToken = default);

        /// <summary>Descarga un item convertido a PDF (<c>/content?format=pdf</c>).</summary>
        Task<byte[]> DownloadAsPdfAsync(
            string itemId, string? driveId = null, CancellationToken cancellationToken = default);

        /// <summary>Borra un item del drive. No lanza si ya no existe.</summary>
        Task DeleteAsync(
            string itemId, string? driveId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Crea la carpeta y todas sus intermedias si no existen. Idempotente.
        ///
        /// Hace falta porque el upload simple de Graph (PUT .../root:/{path}:/content) NO
        /// crea los directorios del camino: si falta uno, responde 404 itemNotFound.
        /// </summary>
        /// <param name="folderPath">Ruta relativa a la raiz del drive, sin barra inicial.</param>
        /// <param name="driveId">Drive destino. <c>null</c> = la biblioteca por defecto.</param>
        Task EnsureFolderAsync(
            string folderPath, string? driveId = null, CancellationToken cancellationToken = default);

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
