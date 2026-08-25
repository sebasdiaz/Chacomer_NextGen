using System.Text.Json.Serialization;

namespace AxxonCustomers.Functions.Models
{
    /// <summary>
    /// Respuesta de F&amp;O sobre una fila de <c>LTMCustTable</c>. Solo interesan los dos
    /// campos de la clave: el resto del payload se arma como diccionario a partir del
    /// mapeo, igual que en CustomersV3, para no mantener a mano una clase espejo de la
    /// tabla del ERP.
    /// </summary>
    public class LtmCustTableRecord
    {
        [JsonPropertyName("AccountNum")]
        public string? AccountNum { get; set; }

        [JsonPropertyName("dataAreaId")]
        public string? DataAreaId { get; set; }
    }
}
