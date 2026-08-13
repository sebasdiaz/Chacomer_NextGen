using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxxonThinkchat.Functions.Models
{
    /// <summary>
    /// Template tal como lo devuelve get_template de Thinkchat.
    ///
    /// PENDIENTE DE CONFIRMAR contra la collection de Postman: los nombres de las
    /// propiedades JSON salen del mapeo 1:1 con las columnas de axx_metatemplates.
    /// Si la API usa otros nombres, se ajusta el [JsonPropertyName] de cada campo
    /// y no hace falta tocar nada mas.
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
