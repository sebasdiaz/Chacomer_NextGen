using System;
using System.Text;

namespace AxxonContacts.Plugins.Services
{
    /// <summary>
    /// Envuelve un payload JSON en el envelope estandar de la EiP.
    ///
    /// Se escribe a mano (StringBuilder) para no agregar dependencias al plugin,
    /// que corre en el Dataverse Sandbox (net462) y evita ILMerge / System.Text.Json.
    /// El contrato del envelope esta en docs/contracts/eip-message-envelope.schema.json
    /// y tipado en Axxon.Eip.Core (Messaging/EipMessage.cs) para el lado consumidor.
    /// </summary>
    public static class EipEnvelope
    {
        private const string SchemaVersion = "1.0";
        private const string SourceDataverse = "dataverse";

        /// <summary>
        /// Devuelve el envelope EiP completo con <paramref name="payloadJson"/> embebido
        /// como objeto en la propiedad "payload".
        /// </summary>
        /// <param name="payloadJson">JSON del payload de dominio (objeto {...}).</param>
        /// <param name="entityType">Tipo de entidad: "contact", "account", ...</param>
        /// <param name="operation">Operacion: "create" | "update".</param>
        /// <param name="partitionKey">Clave de orden = SessionId de Service Bus.</param>
        /// <param name="correlationId">CorrelationId del contexto del plugin (traza e2e).</param>
        public static string Wrap(
            string payloadJson,
            string entityType,
            string operation,
            string partitionKey,
            Guid correlationId)
        {
            var sb = new StringBuilder((payloadJson?.Length ?? 0) + 512);
            sb.Append('{');
            AppendString(sb, "schemaVersion", SchemaVersion);                 sb.Append(',');
            AppendString(sb, "messageId",     Guid.NewGuid().ToString());     sb.Append(',');
            AppendString(sb, "correlationId", correlationId.ToString());      sb.Append(',');
            AppendString(sb, "source",        SourceDataverse);               sb.Append(',');
            AppendString(sb, "entityType",    entityType);                    sb.Append(',');
            AppendString(sb, "operation",     operation);                     sb.Append(',');
            AppendString(sb, "occurredAt",    DateTimeOffset.UtcNow.ToString("O")); sb.Append(',');
            AppendString(sb, "partitionKey",  partitionKey);                  sb.Append(',');
            sb.Append("\"payload\":").Append(string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":");
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"')
              .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                           .Replace("\n", "\\n").Replace("\r", "\\r"))
              .Append('"');
        }
    }
}
