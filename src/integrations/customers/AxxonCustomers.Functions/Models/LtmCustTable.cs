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

    /// <summary>
    /// Fila del catalogo de estados de F&amp;O (<c>AddressStates</c>), contra el que se valida
    /// el <c>stateorprovince</c> que trae la direccion de Dataverse.
    ///
    /// El codigo se llama <c>State</c>, no <c>StateId</c>: <c>StateId</c> es el nombre del
    /// campo destino en <c>LTMCustTable</c>, y usarlo aca devuelve un 400 de OData.
    /// </summary>
    public class FoAddressState
    {
        [JsonPropertyName("State")]
        public string? State { get; set; }
    }
}
