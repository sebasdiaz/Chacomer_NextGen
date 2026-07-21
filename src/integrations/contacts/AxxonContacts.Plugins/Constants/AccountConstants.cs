namespace AxxonContacts.Plugins.Constants
{
    /// <summary>
    /// Nombres logicos de campos de la entidad account. Unica fuente de verdad
    /// para el AccountEventPublisherPlugin (evita magic strings).
    /// </summary>
    public static class AccountConstants
    {
        public const string EntityLogicalName = "account";

        // ── Custom Axxon ────────────────────────────────────────────
        public const string IsMaster        = "axx_ismaster";
        public const string MasterAccountId = "axx_masteraccountid";

        // ── Datos de cuenta ──────────────────────────────────────────
        public const string Name          = "name";
        public const string Telephone1    = "telephone1";
        public const string EmailAddress1 = "emailaddress1";
        public const string Description   = "description";

        // ── Clave de matching / Dual Write ───────────────────────────
        public const string MsdynIdentificationNumber = "msdyn_identificationnumber";
        public const string MsdynCompany               = "msdyn_company";

        // ── Auditoria ────────────────────────────────────────────────
        public const string ModifiedBy = "modifiedby";
        public const string ModifiedOn = "modifiedon";

        /// <summary>Columnas que el plugin recupera del Account para armar el mensaje.</summary>
        public static readonly string[] RequiredColumns = new[]
        {
            IsMaster, MasterAccountId,
            Name, Telephone1, EmailAddress1, Description,
            MsdynIdentificationNumber, MsdynCompany,
            ModifiedBy, ModifiedOn
        };
    }
}
