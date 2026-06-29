namespace AxxonProductGroups.Functions.Configuration
{
    public class AppSettings
    {
        // ── Dataverse ─────────────────────────────────────────────
        public string DataverseUrl { get; set; } = string.Empty;

        public string? DataverseClientId { get; set; }

        public string? DataverseClientSecret { get; set; }

        public bool UseClientSecretAuth =>
            !string.IsNullOrEmpty(DataverseClientId) &&
            !string.IsNullOrEmpty(DataverseClientSecret);

        // ── Sync behavior ────────────────────────────────────────
        /// <summary>
        /// Si es true, ejecuta AssignRequest para setear owningbusinessunit/owningteam
        /// por el team por defecto de la BU correspondiente al dataAreaId.
        /// Requiere que el default team de cada BU tenga prvRead sobre msdyn_productgroup.
        /// </summary>
        public bool AssignOwningBusinessUnit { get; set; } = false;

        // ── Finance & Operations ──────────────────────────────────
        public string FoBaseUrl { get; set; } = string.Empty;

        public string? FoTenantId { get; set; }

        public string? FoClientId { get; set; }

        public string? FoClientSecret { get; set; }
    }
}
