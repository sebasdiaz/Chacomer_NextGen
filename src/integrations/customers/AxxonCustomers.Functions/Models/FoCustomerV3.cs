using System.Text.Json.Serialization;

namespace AxxonCustomers.Functions.Models
{
    /// <summary>
    /// Respuesta de F&amp;O al crear el customer. Solo interesa el CustomerAccount
    /// generado (number sequence) para el write-back a Dataverse.
    ///
    /// El DTO de REQUEST ya no existe: el payload se arma como diccionario a partir del
    /// mapeo de Mappings/customersv3.{entidad}.*.json. Mantener una clase con 28
    /// propiedades sincronizada a mano con el export de Dual Write era duplicar la
    /// fuente de verdad.
    ///
    /// Dato que sobrevive de esa clase y esta en la base del <see cref="Mapping.FoSchemaProvider"/>:
    /// los nombres de propiedad de la API OData no son derivables del export
    /// (MAYUSCULAS). Verificado contra el environment: la propiedad es "A365Sellable",
    /// no "A365SELLABLE".
    /// </summary>
    public class FoCustomerV3CreatedResponse
    {
        [JsonPropertyName("CustomerAccount")]
        public string? CustomerAccount { get; set; }

        [JsonPropertyName("PartyNumber")]
        public string? PartyNumber { get; set; }

        [JsonPropertyName("dataAreaId")]
        public string? DataAreaId { get; set; }
    }
}
