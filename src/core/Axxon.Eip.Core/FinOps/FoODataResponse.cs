using System.Text.Json.Serialization;

namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Pagina de resultados de la OData API de F&amp;O.
    /// </summary>
    public sealed class FoODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
