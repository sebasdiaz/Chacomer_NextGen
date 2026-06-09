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
        /// Nombre de la queue de Service Bus para ContactMasterMatchingFunction.
        /// Ejemplo: contacts
        /// </summary>
        public string ServiceBusQueueName { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de la queue de Service Bus para AccountMasterMatchingFunction.
        /// Ejemplo: account
        /// </summary>
        public string AccountServiceBusQueueName { get; set; } = string.Empty;


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

        // ---- API Oficial de la SET (Subsecretaria de Estado de Tributacion) ----

        /// <summary>
        /// API Key para el servicio oficial de consulta de RUC de la SET Paraguay.
        /// Endpoint: https://servicios.set.gov.py/EsetApiWS/ApiWS/consultaRuc
        /// Configurar en Function App → Configuration → Application Settings como "SetApiKey".
        /// </summary>
        public string? SetApiKey { get; set; }

        // ---- Finance & Operations OData (ReleasedProductsV2 sync) ----

        /// <summary>
        /// URL base del ambiente de F&O.
        /// Ejemplo: https://miempresa.operations.dynamics.com
        /// </summary>
        public string FoBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Schedule CRON para el sync de ReleasedProductsV2.
        /// Formato de 6 partes (segundos incluidos): "0 0 * * * *" = cada hora en punto.
        /// Configurar como "Schedules:ReleasedProductSync" en App Settings.
        /// </summary>
        public string ReleasedProductSyncSchedule { get; set; } = "0 0 * * * *";

        // ---- Solo para DESA cuando se necesita Service Principal para F&O ----

        /// <summary>Tenant ID del AAD. Solo necesario si UseClientSecretAuth = true para F&O.</summary>
        public string? FoTenantId { get; set; }

        /// <summary>Client ID del App Registration con acceso a F&O. Solo para desarrollo local.</summary>
        public string? FoClientId { get; set; }

        /// <summary>Client Secret del App Registration con acceso a F&O. Solo para desarrollo local.</summary>
        public string? FoClientSecret { get; set; }
    }
}
