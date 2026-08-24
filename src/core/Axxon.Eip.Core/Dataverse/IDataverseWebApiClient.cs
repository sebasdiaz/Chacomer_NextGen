using System.Text.Json;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>
    /// Cliente de la Web API (OData v4) de Dataverse.
    ///
    /// Complementa a <see cref="DataverseClientFactory"/> (SDK / IOrganizationService), que
    /// es lo que usa el resto de la EiP. La Web API se usa cuando la consulta se expresa
    /// mucho mejor en OData que en FetchXML — tipicamente cuando hay $expand anidados de
    /// varios niveles, como el arbol Cita -> Dispositivo -> Marca/Modelo/Color.
    /// </summary>
    public interface IDataverseWebApiClient
    {
        /// <summary>
        /// GET de un registro unico. Devuelve null si Dataverse responde 404.
        /// Cualquier otro status de error lanza <see cref="DataverseWebApiException"/>.
        /// </summary>
        /// <param name="relativeUrl">Ruta relativa a /api/data/v9.2/, ej: "accounts(guid)?$select=name".</param>
        /// <param name="label">Etiqueta para logs y mensajes de error, ej: "Cita".</param>
        Task<JsonElement?> GetRecordAsync(string relativeUrl, string label, CancellationToken cancellationToken = default);

        /// <summary>
        /// GET de una coleccion. Devuelve el contenido de "value".
        /// Una coleccion vacia es un resultado valido; un error HTTP lanza.
        /// </summary>
        Task<IReadOnlyList<JsonElement>> GetArrayAsync(string relativeUrl, string label, CancellationToken cancellationToken = default);

        /// <summary>
        /// POST de un registro. Devuelve el GUID que Dataverse expone en el header OData-EntityId.
        /// </summary>
        /// <param name="entitySet">EntitySet destino, ej: "sharepointdocumentlocations".</param>
        /// <param name="payload">Objeto a serializar como body.</param>
        Task<Guid> CreateAsync(string entitySet, object payload, string label, CancellationToken cancellationToken = default);
    }
}
