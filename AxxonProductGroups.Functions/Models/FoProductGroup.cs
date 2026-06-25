using System.Text.Json.Serialization;

namespace AxxonProductGroups.Functions.Models
{
    /// <summary>
    /// DTO que representa un registro de la entidad ProductGroups de F&O.
    /// Solo se mapean los campos requeridos por la integracion hacia msdyn_productgroup.
    /// </summary>
    public class FoProductGroup
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("GroupId")]
        public string GroupId { get; set; } = string.Empty;

        [JsonPropertyName("GroupName")]
        public string? GroupName { get; set; }
    }

    public class FoODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
