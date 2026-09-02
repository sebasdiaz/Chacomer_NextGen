using System.Text.Json.Serialization;

namespace AxxonCustomerCredit.Functions.Models
{
    /// <summary>
    /// Un registro de <c>DevAxCustCreditResolutions</c>: la resolucion de un analista
    /// sobre una solicitud de credito.
    ///
    /// <b>Es la unica de las cuatro entidades que no tiene <c>CustomerAccount</c>.</b> Se
    /// llega a ella por <see cref="SolicitudId"/>, que es el <c>RequestId</c> del plan
    /// (<see cref="FoCreditoPlan.RequestId"/>).
    /// </summary>
    public sealed class FoCreditoResolucion
    {
        // --- Clave de la entidad: (dataAreaId, SolicitudId, ResolutionId) ---

        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        /// <summary>Solicitud resuelta. Una solicitud puede tener mas de una resolucion.</summary>
        [JsonPropertyName("SolicitudId")]
        public string SolicitudId { get; set; } = string.Empty;

        [JsonPropertyName("ResolutionId")]
        public string ResolutionId { get; set; } = string.Empty;

        /// <summary>
        /// Enum <c>DevAxCustCreditResolutionStatus</c>: "PendingInfo", "Approved" o
        /// "Rejected".
        /// </summary>
        [JsonPropertyName("Resolution")]
        public string? Resolution { get; set; }

        [JsonPropertyName("ResolutionDateTime")]
        public DateTimeOffset? ResolutionDateTime { get; set; }

        [JsonPropertyName("AnalystId")]
        public string? AnalystId { get; set; }

        /// <summary>Enum <c>DevAxCustCreditRiskClassification</c>: "None", "Low", "Medium" o "High".</summary>
        [JsonPropertyName("RiskClassification")]
        public string? RiskClassification { get; set; }

        [JsonPropertyName("Score")]
        public decimal Score { get; set; }

        [JsonPropertyName("RejectionReason")]
        public string? RejectionReason { get; set; }

        [JsonPropertyName("Comments")]
        public string? Comments { get; set; }

        [JsonPropertyName("SpecialConditions")]
        public string? SpecialConditions { get; set; }

        // --- Contrapropuesta del analista. Solo tienen sentido con PlanModified = "Yes". ---

        /// <summary>Enum <c>NoYes</c>: si el analista cambio el plan pedido.</summary>
        [JsonPropertyName("PlanModified")]
        public string? PlanModified { get; set; }

        [JsonPropertyName("NewPlanId")]
        public string? NewPlanId { get; set; }

        [JsonPropertyName("NewAmount")]
        public decimal NewAmount { get; set; }

        [JsonPropertyName("NewInstallmentCount")]
        public int NewInstallmentCount { get; set; }
    }
}
