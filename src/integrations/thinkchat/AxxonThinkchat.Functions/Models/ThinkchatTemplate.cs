using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxxonThinkchat.Functions.Models
{
    /// <summary>
    /// Template tal como lo devuelve la accion get_templates de Thinkchat.
    /// Nombres verificados contra la collection de Postman del proveedor.
    ///
    /// El response trae ademas un "date" (fecha de alta del template) que no se mapea:
    /// axx_metatemplates no tiene columna para eso.
    /// </summary>
    public class ThinkchatTemplate
    {
        /// <summary>Identificador del template. Clave del upsert contra axx_id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// La API manda numeros aca. Se deja como JsonElement y se guarda el JSON crudo
        /// en axx_variables (columna de texto), asi sirve tanto si viene un numero suelto
        /// como si viene un array — sin tener que adivinar cual de las dos.
        /// </summary>
        [JsonPropertyName("variables")]
        public JsonElement? Variables { get; set; }

        /// <summary>Representacion textual de <see cref="Variables"/> para la columna de Dataverse.</summary>
        public string? VariablesAsText =>
            Variables is null || Variables.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : Variables.Value.ValueKind == JsonValueKind.String
                    ? Variables.Value.GetString()
                    : Variables.Value.GetRawText();
    }
}
