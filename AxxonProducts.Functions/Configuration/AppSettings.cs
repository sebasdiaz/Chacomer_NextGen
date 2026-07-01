namespace AxxonProducts.Functions.Configuration
{
    public class AppSettings
    {
        public string DataverseUrl { get; set; } = string.Empty;

        public string? DataverseClientId { get; set; }

        public string? DataverseClientSecret { get; set; }

        public bool UseClientSecretAuth =>
            !string.IsNullOrEmpty(DataverseClientId) &&
            !string.IsNullOrEmpty(DataverseClientSecret);

        /// <summary>
        /// Si es true, ProductGroupSyncService ejecuta AssignRequest para setear
        /// owningbusinessunit/owningteam por el team por defecto de la BU correspondiente
        /// al dataAreaId. Requiere que el default team de cada BU tenga prvRead sobre
        /// msdyn_productgroup.
        /// </summary>
        public bool AssignOwningBusinessUnit { get; set; } = false;

        public string FoBaseUrl { get; set; } = string.Empty;

        public string? FoTenantId { get; set; }

        public string? FoClientId { get; set; }

        public string? FoClientSecret { get; set; }
    }
}
