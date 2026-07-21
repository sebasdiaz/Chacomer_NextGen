using System.Text.Json.Serialization;

namespace AxxonCustomerGroups.Functions.Models
{
    /// <summary>
    /// DTO que representa un registro de la entidad CustomerGroups de F&O.
    /// Campos segun CustomerGroups_Mapping.json (la lectura cross-company
    /// incluye dataAreaId para resolver la compania en Dataverse).
    /// </summary>
    public class FoCustomerGroup
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("CustomerGroupId")]
        public string CustomerGroupId { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string? Description { get; set; }

        /// <summary>Enum NoYes de F&O: "Yes" / "No".</summary>
        [JsonPropertyName("IsSalesTaxIncludedInPrice")]
        public string? IsSalesTaxIncludedInPrice { get; set; }

        [JsonPropertyName("PaymentTermId")]
        public string? PaymentTermId { get; set; }

        [JsonPropertyName("ClearingPeriodPaymentTermName")]
        public string? ClearingPeriodPaymentTermName { get; set; }
    }
}
