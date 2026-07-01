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

        /// <summary>
        /// Tipo de documento (OptionSet). Forma parte de la clave de matching junto con
        /// msdyn_identificationnumber (alternate key: numero + tipo de documento).
        /// </summary>
        [JsonPropertyName("axxTipoDocumento")]
        public int? AxxTipoDocumento { get; set; }

        // ── Auditoria ────────────────────────────────────────────────
        [JsonPropertyName("modifiedBy")]
        public Guid? ModifiedBy { get; set; }

        [JsonPropertyName("modifiedOn")]
        public DateTimeOffset? ModifiedOn { get; set; }
    }
}
