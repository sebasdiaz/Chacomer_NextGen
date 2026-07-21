namespace AxxonContacts.Plugins.Models
{
    /// <summary>
    /// Payload JSON del evento de Account Raw. Snapshot que viaja como payload
    /// del envelope EiP en Service Bus.
    ///
    /// Los nombres JSON (ver SerializeToJson en AccountEventPublisherPlugin) deben
    /// coincidir con AxxonContacts.Functions.Models.AccountEventMessage.
    /// </summary>
    public class AccountEventMessage
    {
        // ── Identidad ────────────────────────────────────────────────
        public string AccountId { get; set; }                // Guid como string

        // ── Metadata del evento ──────────────────────────────────────
        public string TriggerMessage { get; set; }           // Create | Update
        public string PublishedAt    { get; set; }           // ISO 8601

        /// <summary>true si msdyn_identificationnumber vino en el Target del evento.</summary>
        public bool IdentificationNumberChanged { get; set; }

        // ── Datos de cuenta ──────────────────────────────────────────
        public string Name          { get; set; }
        public string Telephone1    { get; set; }
        public string EmailAddress1 { get; set; }
        public string Description   { get; set; }

        // ── Control Master/Raw ───────────────────────────────────────
        public bool   IsMaster        { get; set; }
        public string MasterAccountId { get; set; }          // Guid

        // ── Dual Write ───────────────────────────────────────────────
        public string MsdynCompany              { get; set; } // Guid — account/company
        public string MsdynIdentificationNumber { get; set; } // clave de matching y SessionId

        // ── Auditoria (solo lectura — no se propaga al Master) ───────
        public string ModifiedBy { get; set; }               // Guid — systemuser
        public string ModifiedOn { get; set; }               // ISO 8601
    }
}
