namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Helpers de sintaxis OData.
    /// </summary>
    public static class FoOData
    {
        /// <summary>
        /// Escapa un literal string para usar dentro de un $filter.
        /// OData escapa la comilla simple duplicandola dentro del literal.
        /// </summary>
        public static string EscapeLiteral(string value) => value.Replace("'", "''");
    }
}
