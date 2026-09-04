using System.Text.Json.Serialization;

namespace AxxonContacts.Functions.Models
{
    /// <summary>
    /// Modelo interno que representa el estado de una Account Raw en el momento del evento.
    /// Populado por AccountExecutionContextParser a partir del RemoteExecutionContext de Dataverse.
    /// </summary>
    public class AccountEventMessage
    {
        // ── Identidad ────────────────────────────────────────────────
        [JsonPropertyName("accountId")]
        public Guid AccountId { get; set; }

        // ── Metadata del evento ──────────────────────────────────────
        [JsonPropertyName("triggerMessage")]
        public string TriggerMessage { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset PublishedAt { get; set; }

        /// <summary>
        /// true cuando msdyn_identificationnumber estaba en el Target del evento.
        /// En eventos Update indica que el campo de identificacion fue establecido en esta operacion.
        /// </summary>
        [JsonPropertyName("identificationNumberChanged")]
        public bool IdentificationNumberChanged { get; set; }

        // ── Datos de cuenta ──────────────────────────────────────────
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("telephone1")]
        public string? Telephone1 { get; set; }

        [JsonPropertyName("emailAddress1")]
        public string? EmailAddress1 { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// axx_lugarcomercial. Lookup a la tabla axx_lugarcomercial: solo viaja el Id.
        /// </summary>
        [JsonPropertyName("axxLugarComercial")]
        public Guid? AxxLugarComercial { get; set; }

        /// <summary>
        /// axx_tipopersoneriajuridica. Lookup a la tabla axx_personeriajuridia: solo viaja el Id.
        /// </summary>
        [JsonPropertyName("axxTipoPersoneriaJuridica")]
        public Guid? AxxTipoPersoneriaJuridica { get; set; }

        // ── Domicilio (bloque address1_*) ────────────────────────────
        // Se copia tal cual del raw al master. Los nombres siguen el logical name
        // de Dataverse: ojo que el campo de departamento/estado es
        // address1_stateorprovince, no address1_stateprovince.
        [JsonPropertyName("address1Line1")]
        public string? Address1Line1 { get; set; }

        [JsonPropertyName("address1Line2")]
        public string? Address1Line2 { get; set; }

        [JsonPropertyName("address1Line3")]
        public string? Address1Line3 { get; set; }

        [JsonPropertyName("address1City")]
        public string? Address1City { get; set; }

        [JsonPropertyName("address1County")]
        public string? Address1County { get; set; }

        [JsonPropertyName("address1StateOrProvince")]
        public string? Address1StateOrProvince { get; set; }

        [JsonPropertyName("address1PostalCode")]
        public string? Address1PostalCode { get; set; }

        [JsonPropertyName("address1Country")]
        public string? Address1Country { get; set; }

        /// <summary>address1_latitude. Tipo Double en Dataverse (rango -90..90).</summary>
        [JsonPropertyName("address1Latitude")]
        public double? Address1Latitude { get; set; }

        /// <summary>address1_longitude. Tipo Double en Dataverse (rango -180..180).</summary>
        [JsonPropertyName("address1Longitude")]
        public double? Address1Longitude { get; set; }

        /// <summary>True si llego algun dato de domicilio en el evento.</summary>
        [JsonIgnore]
        public bool HasAddress =>
            !string.IsNullOrWhiteSpace(Address1Line1)   ||
            !string.IsNullOrWhiteSpace(Address1Line2)   ||
            !string.IsNullOrWhiteSpace(Address1Line3)   ||
            !string.IsNullOrWhiteSpace(Address1City)    ||
            !string.IsNullOrWhiteSpace(Address1County)  ||
            !string.IsNullOrWhiteSpace(Address1StateOrProvince) ||
            !string.IsNullOrWhiteSpace(Address1PostalCode)      ||
            !string.IsNullOrWhiteSpace(Address1Country) ||
            Address1Latitude.HasValue || Address1Longitude.HasValue;

        // ── Control Master/Raw ───────────────────────────────────────
        [JsonPropertyName("isMaster")]
        public bool IsMaster { get; set; }

        [JsonPropertyName("masterAccountId")]
        public Guid? MasterAccountId { get; set; }

        // ── Dual Write ───────────────────────────────────────────────
        [JsonPropertyName("msdynCompany")]
        public Guid? MsdynCompany { get; set; }

        // ── Clave de matching ────────────────────────────────────────
        /// <summary>Campo de identificacion unica. Coincide con el SessionId de Service Bus.</summary>
        [JsonPropertyName("msdynIdentificationNumber")]
        public string? MsdynIdentificationNumber { get; set; }

        // ── Auditoria ────────────────────────────────────────────────
        [JsonPropertyName("modifiedBy")]
        public Guid? ModifiedBy { get; set; }

        [JsonPropertyName("modifiedOn")]
        public DateTimeOffset? ModifiedOn { get; set; }
    }
}
