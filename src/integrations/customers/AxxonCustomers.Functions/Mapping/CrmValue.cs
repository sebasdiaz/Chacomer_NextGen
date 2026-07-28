using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Conversion de valores de Dataverse al payload de F&amp;O.
    /// </summary>
    internal static class CrmValue
    {
        /// <summary>
        /// String canonico de un valor de Dataverse, usado como clave de los value maps.
        /// Los maps de Dual Write escriben los booleanos como "True"/"False" y los
        /// OptionSet como el entero, asi que la representacion tiene que coincidir.
        /// </summary>
        public static string? RenderCanonical(object? value) => value switch
        {
            null                => null,
            bool b              => b ? "True" : "False",
            OptionSetValue osv  => osv.Value.ToString(CultureInfo.InvariantCulture),
            Money money         => money.Value.ToString(CultureInfo.InvariantCulture),
            string s            => s,
            DateTime dt         => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable f      => f.ToString(null, CultureInfo.InvariantCulture),
            _                   => value.ToString()
        };

        /// <summary>
        /// Valor listo para serializar en el POST a F&amp;O. Un EntityReference aca es
        /// un error de mapeo: un lookup se declara con path "campo.atributoRelacionado".
        /// </summary>
        public static object? ToPayloadValue(object? value, string targetField) => value switch
        {
            null                     => null,
            Money money              => money.Value,
            OptionSetValue osv       => osv.Value,
            DateTime dt              => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            EntityReference          => throw new InvalidOperationException(
                                            $"El campo '{targetField}' apunta a un lookup pero esta mapeado como " +
                                            "'direct'. Usar el path 'atributo.atributoRelacionado' o kind 'lookup'."),
            bool or int or long or decimal or double or float or string => value,
            _                        => value.ToString()
        };

        /// <summary>Convierte un literal del overlay (constante o condicion) a CLR.</summary>
        public static object? FromJson(JsonElement element, string context) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Null   => null,
            _ => throw new InvalidOperationException(
                     $"{context}: solo se admiten string, numero, booleano o null (llego {element.ValueKind}).")
        };
    }
}
