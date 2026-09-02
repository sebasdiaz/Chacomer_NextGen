using System.Text.Json.Serialization;

namespace AxxonCustomerCredit.Functions.Models
{
    /// <summary>
    /// Un registro de <c>DevAxCustCreditInstallments</c>: una cuota de un plan otorgado.
    /// Cuelga del <c>CreditId</c> de <see cref="FoCreditoPlan"/>.
    /// </summary>
    public sealed class FoCreditoCuota
    {
        // --- Clave de la entidad: (dataAreaId, CreditId, InstallmentNumber) ---

        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("CreditId")]
        public string CreditId { get; set; } = string.Empty;

        [JsonPropertyName("InstallmentNumber")]
        public int InstallmentNumber { get; set; }

        /// <summary>Identificador propio de la cuota en F&amp;O, distinto del numero de orden.</summary>
        [JsonPropertyName("InstallmentId")]
        public string? InstallmentId { get; set; }

        /// <summary>
        /// Denormalizado por F&amp;O en la cuota. Es lo que permite pedir todas las cuotas
        /// de un cliente sin resolver antes sus planes.
        /// </summary>
        [JsonPropertyName("CustomerAccount")]
        public string? CustomerAccount { get; set; }

        /// <summary>
        /// Enum <c>DevAxCustCreditInstallmentStatus</c>: "Pending", "Paid", "Overdue" o
        /// "Refinanced".
        /// </summary>
        [JsonPropertyName("InstallmentStatus")]
        public string? InstallmentStatus { get; set; }

        [JsonPropertyName("DueDate")]
        public DateTimeOffset? DueDate { get; set; }

        /// <summary>Fecha de pago. Trae la fecha centinela de F&amp;O si la cuota no se pago.</summary>
        [JsonPropertyName("PaymentDate")]
        public DateTimeOffset? PaymentDate { get; set; }

        [JsonPropertyName("PaymentVoucher")]
        public string? PaymentVoucher { get; set; }

        // --- Importes. TotalAmount es el total de la cuota; el resto lo desglosa. ---

        [JsonPropertyName("TotalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("CapitalAmount")]
        public decimal CapitalAmount { get; set; }

        [JsonPropertyName("InterestAmount")]
        public decimal InterestAmount { get; set; }

        [JsonPropertyName("PenaltyInterest")]
        public decimal PenaltyInterest { get; set; }

        [JsonPropertyName("ExemptedInterest")]
        public decimal ExemptedInterest { get; set; }

        [JsonPropertyName("PendingBalance")]
        public decimal PendingBalance { get; set; }
    }
}
