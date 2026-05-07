namespace AxxonContacts.Functions.Configuration
{
    /// <summary>
    /// Variables de entorno de la Azure Function.
    /// En produccion se configuran en la Function App → Configuration → Application Settings.
    /// En local se leen desde local.settings.json.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// URL del environment de Dataverse.
        /// Ejemplo: https://tuorg.crm.dynamics.com
        /// </summary>
        public string DataverseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de la queue de Service Bus.
        /// Ejemplo: contact-master-matching
        /// </summary>
        public string ServiceBusQueueName { get; set; } = string.Empty;

        // ---- Solo para DESA / fallback cuando Managed Identity no esta disponible ----

        /// <summary>Client ID del App Registration. Solo para desarrollo local.</summary>
        public string? DataverseClientId { get; set; }

        /// <summary>Client Secret del App Registration. Solo para desarrollo local.</summary>
        public string? DataverseClientSecret { get; set; }

        /// <summary>
        /// True si se deben usar credenciales de Service Principal en lugar de Managed Identity.
        /// Se activa automaticamente si DataverseClientId y DataverseClientSecret estan presentes.
        /// </summary>
        public bool UseClientSecretAuth =>
            !string.IsNullOrEmpty(DataverseClientId) &&
            !string.IsNullOrEmpty(DataverseClientSecret);
    }
}
