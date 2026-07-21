using System.Text.Json;
using Axxon.Eip.Core.Messaging;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Deteccion y parseo del envelope EiP recibido por Service Bus.
    ///
    /// Durante la transicion conviven dos formatos en la misma queue:
    ///   - Envelope EiP (nuevo): lo emite el plugin thin. Tiene "schemaVersion".
    ///   - RemoteExecutionContext (legacy): lo emite el Service Endpoint nativo.
    /// <see cref="IsEnvelope"/> distingue uno de otro para rutear al parser correcto.
    /// </summary>
    public static class EipEnvelopeParser
    {
        /// <summary>
        /// true si el cuerpo es un envelope EiP (tiene "schemaVersion" en la raiz).
        /// El RemoteExecutionContext nativo no tiene esa propiedad.
        /// </summary>
        public static bool IsEnvelope(string raw)
        {
            var json = StripFraming(raw);
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("schemaVersion", out _);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Deserializa el envelope y devuelve su payload tipado (o null).</summary>
        public static TPayload? ParsePayload<TPayload>(string raw)
        {
            var json = StripFraming(raw);
            var envelope = JsonSerializer.Deserialize<EipMessage<TPayload>>(
                json, EipMessageDefaults.SerializerOptions);
            return envelope is null ? default : envelope.Payload;
        }

        // Dataverse puede envolver el body con framing AMQP; el JSON real
        // arranca en el primer '{' (mismo criterio que los parsers legacy).
        private static string StripFraming(string raw)
        {
            var start = raw.IndexOf('{');
            return start >= 0 ? raw[start..] : raw;
        }
    }
}
