using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axxon.Eip.Core.Messaging
{
    /// <summary>
    /// Envelope estandar de mensajes de la EiP. Todo mensaje que viaja por el
    /// backbone asincronico (Service Bus) se envuelve en esta estructura,
    /// independientemente del sistema origen (Dataverse, Magento, VTex, F&amp;O, ...).
    ///
    /// El envelope es AGNOSTICO a la fuente: separa el sobre (routing, trazabilidad,
    /// idempotencia) del <see cref="Payload"/> (el DTO propio de cada entidad/integracion).
    ///
    /// Para el paso de dispatch (leer el sobre sin conocer aun el tipo del payload)
    /// usar <see cref="EipMessage"/> (no generico, payload como JsonElement) y luego
    /// materializar el tipo concreto.
    /// </summary>
    /// <typeparam name="TPayload">DTO especifico de la entidad/integracion.</typeparam>
    public sealed class EipMessage<TPayload>
    {
        /// <summary>Version del contrato del envelope. Ver docs/contracts.</summary>
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = EipMessageDefaults.SchemaVersion;

        /// <summary>Identificador unico del mensaje. Base de la idempotencia en el consumer.</summary>
        [JsonPropertyName("messageId")]
        public string MessageId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Correlaciona el mensaje con la operacion de negocio que lo origino.
        /// Fluye punta a punta (APIM → Function → Dataverse/F&amp;O) y se usa en
        /// Application Insights para trazar el flujo completo.
        /// </summary>
        [JsonPropertyName("correlationId")]
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Sistema origen del evento. Ver <see cref="EipSource"/>.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        /// <summary>Tipo de entidad de negocio (contact, customer, product, ...).</summary>
        [JsonPropertyName("entityType")]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>Operacion. Ver <see cref="EipOperation"/>.</summary>
        [JsonPropertyName("operation")]
        public string Operation { get; set; } = string.Empty;

        /// <summary>Instante en que ocurrio el evento en el origen (UTC, ISO 8601).</summary>
        [JsonPropertyName("occurredAt")]
        public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Clave de particion para ordenamiento. Cuando la queue tiene sessions,
        /// este valor se usa como SessionId (ej: numero de identificacion del
        /// cliente para procesar en orden todos los eventos de un mismo cliente).
        /// </summary>
        [JsonPropertyName("partitionKey")]
        public string? PartitionKey { get; set; }

        /// <summary>Metadata opcional (usuario iniciador, app origen, etc.).</summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }

        /// <summary>Contenido de negocio del mensaje.</summary>
        [JsonPropertyName("payload")]
        public TPayload? Payload { get; set; }

        /// <summary>
        /// Crea un envelope con messageId/correlationId/occurredAt por defecto.
        /// </summary>
        public static EipMessage<TPayload> Create(
            string source,
            string entityType,
            string operation,
            TPayload payload,
            string? partitionKey = null,
            string? correlationId = null)
            => new()
            {
                Source        = source,
                EntityType    = entityType,
                Operation     = operation,
                Payload       = payload,
                PartitionKey  = partitionKey,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
    }

    /// <summary>
    /// Envelope sin materializar el payload — para el paso de dispatch: leer el
    /// sobre (source/entityType/operation) y recien despues deserializar el
    /// <see cref="Payload"/> al tipo concreto con <see cref="GetPayload{T}"/>.
    /// </summary>
    public sealed class EipMessage
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = EipMessageDefaults.SchemaVersion;

        [JsonPropertyName("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [JsonPropertyName("correlationId")]
        public string CorrelationId { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("entityType")]
        public string EntityType { get; set; } = string.Empty;

        [JsonPropertyName("operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonPropertyName("occurredAt")]
        public DateTimeOffset OccurredAt { get; set; }

        [JsonPropertyName("partitionKey")]
        public string? PartitionKey { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }

        /// <summary>Materializa el payload al tipo concreto.</summary>
        public T? GetPayload<T>() =>
            Payload.Deserialize<T>(EipMessageDefaults.SerializerOptions);
    }

    public static class EipMessageDefaults
    {
        public const string SchemaVersion = "1.0";

        /// <summary>Opciones de serializacion estandar del envelope (camelCase).</summary>
        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
