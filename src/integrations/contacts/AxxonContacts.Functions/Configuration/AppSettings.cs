namespace AxxonContacts.Functions.Configuration
{
    /// <summary>
    /// Settings propios de esta integracion. La conexion a Dataverse se configura
    /// via Axxon.Eip.Core (DataverseUrl, DataverseClientId, DataverseClientSecret).
    ///
    /// Las queues de Service Bus de los triggers se resuelven por binding
    /// expression (%ServiceBusQueueName% / %AccountServiceBusQueueName%)
    /// directamente desde los Application Settings del host — no pasan por aca.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// API Key para el servicio oficial de consulta de RUC de la SET Paraguay.
        /// Endpoint: https://servicios.set.gov.py/EsetApiWS/ApiWS/consultaRuc
        /// En Azure se resuelve desde Key Vault (secret "SetApiKey").
        /// </summary>
        public string? SetApiKey { get; set; }
    }
}
