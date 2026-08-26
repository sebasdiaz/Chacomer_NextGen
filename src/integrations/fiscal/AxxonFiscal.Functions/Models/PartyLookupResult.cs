using System.Text.Json.Serialization;

namespace AxxonFiscal.Functions.Models
{
    /// <summary>
    /// Una parte (contact o account) encontrada en Dataverse para un RUC.
    ///
    /// Un mismo RUC devuelve normalmente varias filas: el master mas los raws que
    /// cuelgan de el, uno por legal entity. Por eso el resultado es una lista y no un
    /// registro unico — ver [Contacts](docs/wiki/integraciones/contacts.md).
    /// </summary>
    public sealed class PartyLookupResult
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        /// <summary>Logical name de la tabla: "contact" o "account".</summary>
        [JsonPropertyName("entidad")]
        public string Entidad { get; init; } = string.Empty;

        /// <summary>
        /// "Fisica" para contact, "Juridica" para account. Se deriva de la tabla:
        /// el modelo no tiene un campo propio de personeria.
        /// </summary>
        [JsonPropertyName("tipoPersona")]
        public string TipoPersona { get; init; } = string.Empty;

        /// <summary>fullname del contact o name del account.</summary>
        [JsonPropertyName("nombre")]
        public string? Nombre { get; init; }

        /// <summary>msdyn_identificationnumber, tal cual esta guardado ("80054203-7").</summary>
        [JsonPropertyName("identificationNumber")]
        public string? IdentificationNumber { get; init; }

        /// <summary>axx_ismaster: distingue el master de los raws con el mismo RUC.</summary>
        [JsonPropertyName("esMaster")]
        public bool EsMaster { get; init; }
    }

    /// <summary>Respuesta del endpoint: el RUC consultado y lo que se encontro.</summary>
    public sealed class PartyLookupResponse
    {
        [JsonPropertyName("ruc")]
        public string Ruc { get; init; } = string.Empty;

        [JsonPropertyName("cantidad")]
        public int Cantidad => Resultados.Count;

        [JsonPropertyName("resultados")]
        public IReadOnlyList<PartyLookupResult> Resultados { get; init; } = [];
    }
}
