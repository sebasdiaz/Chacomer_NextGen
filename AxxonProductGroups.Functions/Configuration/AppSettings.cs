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

        // ── Finance & Operations ──────────────────────────────────
        public string FoBaseUrl { get; set; } = string.Empty;

        public string? FoTenantId { get; set; }

        public string? FoClientId { get; set; }

        public string? FoClientSecret { get; set; }
    }
}
