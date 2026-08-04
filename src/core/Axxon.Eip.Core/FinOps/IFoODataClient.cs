namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Cliente generico de la OData API de Finance &amp; Operations.
    /// Autentica con Managed Identity (o Client Secret en DESA) via OAuth2
    /// contra el tenant de F&amp;O y maneja paginacion con @odata.nextLink.
    /// </summary>
    public interface IFoODataClient
    {
        /// <summary>
        /// Lee todos los registros que matchean la consulta, paginando con
        /// @odata.nextLink de forma transparente.
        /// </summary>
        IAsyncEnumerable<T> QueryAsync<T>(
            FoODataQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Devuelve el primer registro que matchea la consulta ($top=1),
        /// o null si no hay coincidencias.
        /// </summary>
        Task<T?> FindFirstAsync<T>(
            FoODataQuery query,
            CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Inserta un registro en el entity set indicado y devuelve la respuesta
        /// de F&amp;O deserializada (incluye los campos generados por el ERP).
        /// </summary>
        Task<TResponse> CreateAsync<TEntity, TResponse>(
            string entitySet,
            TEntity entity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Actualiza (PATCH) un registro existente. Solo se mandan los campos presentes
        /// en <paramref name="entity"/>: F&amp;O deja intacto todo lo que no viaja en el body.
        ///
        /// <paramref name="entityKey"/> es el segmento de clave ya armado con
        /// <see cref="FoOData.EntityKey"/>, ej: <c>(dataAreaId='cha',CustomerAccount='C0001')</c>.
        /// </summary>
        Task UpdateAsync<TEntity>(
            string entitySet,
            string entityKey,
            TEntity entity,
            CancellationToken cancellationToken = default);
    }
}
