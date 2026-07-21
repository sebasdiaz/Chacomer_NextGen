namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Consulta sobre un entity set de la OData API de F&amp;O.
    /// </summary>
    public sealed class FoODataQuery
    {
        public FoODataQuery(string entitySet) => EntitySet = entitySet;

        /// <summary>Entity set de F&amp;O. Ejemplo: "ReleasedProductsV2", "CustomerGroups".</summary>
        public string EntitySet { get; }

        /// <summary>
        /// Incluir registros de todas las companias del ambiente. Sin esto F&amp;O
        /// filtra por la compania default del caller. Default: true.
        /// </summary>
        public bool CrossCompany { get; init; } = true;

        /// <summary>Expresion $filter (sin escapar la URL — se escapa al armarla).</summary>
        public string? Filter { get; init; }

        /// <summary>Lista de campos para $select. Ejemplo: "CustomerAccount,PartyNumber".</summary>
        public string? Select { get; init; }

        /// <summary>Tamano de pagina ($top). Default: 1000.</summary>
        public int PageSize { get; init; } = 1000;
    }
}
