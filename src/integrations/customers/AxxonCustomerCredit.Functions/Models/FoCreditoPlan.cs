using System.Text.Json.Serialization;

namespace AxxonCustomerCredit.Functions.Models
{
    /// <summary>
    /// Un registro de <c>DevAxCustCreditGrantedPlans</c>: un plan de credito ya otorgado.
    /// Es la cabecera de las cuotas (<see cref="FoCreditoCuota"/>), que cuelgan del mismo
    /// <c>CreditId</c>.
    /// </summary>
    public sealed class FoCreditoPlan
    {
        // --- Clave de la entidad: (dataAreaId, CreditId) ---

        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("CreditId")]
        public string CreditId { get; set; } = string.Empty;

        [JsonPropertyName("CustomerAccount")]
        public string? CustomerAccount { get; set; }

        /// <summary>
        /// Solicitud que dio origen al plan. Es el mismo id que
        /// <see cref="FoCreditoResolucion.SolicitudId"/>: por ahi se cruza el plan con su
        /// resolucion, porque la resolucion no guarda el <c>CreditId</c>.
        /// </summary>
        [JsonPropertyName("RequestId")]
        public string? RequestId { get; set; }

        /// <summary>
        /// Enum <c>DevAxCustCreditGrantedPlanStatus</c>: "Invoiced", "Overdue",
        /// "Cancelled" o "Refinanced".
        /// </summary>
        [JsonPropertyName("GrantedPlanStatus")]
        public string? GrantedPlanStatus { get; set; }

        /// <summary>Texto libre del ERP, no un enum: se publica tal cual.</summary>
        [JsonPropertyName("ComplianceStatus")]
        public string? ComplianceStatus { get; set; }

        [JsonPropertyName("GrantDate")]
        public DateTimeOffset? GrantDate { get; set; }

        [JsonPropertyName("FirstInstallmentDate")]
        public DateTimeOffset? FirstInstallmentDate { get; set; }

        [JsonPropertyName("TotalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("PendingBalance")]
        public decimal PendingBalance { get; set; }

        [JsonPropertyName("TotalInstallments")]
        public int TotalInstallments { get; set; }

        [JsonPropertyName("PaidInstallments")]
        public int PaidInstallments { get; set; }

        [JsonPropertyName("OverdueInstallments")]
        public int OverdueInstallments { get; set; }

        [JsonPropertyName("OverdueDays")]
        public int OverdueDays { get; set; }
    }
}
