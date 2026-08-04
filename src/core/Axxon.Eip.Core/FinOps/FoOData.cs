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

        /// <summary>
        /// Arma el segmento de clave de una entidad para la URL:
        /// <c>(dataAreaId='cha',CustomerAccount='C0001')</c>.
        ///
        /// Las entidades de F&amp;O tienen clave compuesta y casi siempre incluyen
        /// <c>dataAreaId</c>: sin la compania, el PATCH no encuentra el registro.
        /// Cada valor se escapa como literal OData (comilla duplicada) y despues se
        /// codifica para la URL, porque el segmento viaja en el path.
        /// </summary>
        public static string EntityKey(params (string Name, string Value)[] keys)
        {
            if (keys is null || keys.Length == 0)
                throw new ArgumentException("La clave de la entidad no puede estar vacia.", nameof(keys));

            var parts = keys.Select(k =>
            {
                if (string.IsNullOrWhiteSpace(k.Value))
                    throw new ArgumentException(
                        $"El componente '{k.Name}' de la clave no tiene valor.", nameof(keys));

                return $"{k.Name}='{Uri.EscapeDataString(EscapeLiteral(k.Value))}'";
            });

            return $"({string.Join(",", parts)})";
        }
    }
}
