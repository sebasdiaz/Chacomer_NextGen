using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace AxxonContacts.Plugins.Services
{
    /// <summary>
    /// Publica mensajes en Azure Service Bus usando la REST API con SAS token.
    ///
    /// Razon por la que no usamos Azure.Messaging.ServiceBus SDK:
    ///   El plugin corre en Dataverse Sandbox (net462). Agregar el SDK requeriria
    ///   ILMerge de todas sus dependencias en el assembly del plugin, lo cual
    ///   complica el build y engrosa el DLL innecesariamente.
    ///   La REST API de Service Bus es estable, simple y no requiere dependencias adicionales.
    ///
    /// Prerequisitos:
    ///   - Policy de SAS con permiso "Send" sobre la queue.
    ///   - Connection string en el formato estandar de Service Bus.
    ///   - Queue con sessions habilitadas (SessionId = msdyn_identificationnumber).
    /// </summary>
    public class ServiceBusPublisher
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly string _endpoint;      // https://namespace.servicebus.windows.net
        private readonly string _queueName;
        private readonly string _keyName;
        private readonly string _keyValue;
        private readonly ITracingService _tracing;

        /// <summary>
        /// Inicializa el publisher parseando el connection string estandar de Service Bus.
        /// Formato esperado:
        ///   Endpoint=sb://namespace.servicebus.windows.net/;SharedAccessKeyName=SendOnly;SharedAccessKey=xxxxx
        ///
        /// El connection string debe estar en el Secure Configuration del plugin step en PRT.
        /// Formato del Secure Config:
        ///   {connectionString}|{queueName}
        ///   Ejemplo:
        ///   Endpoint=sb://axxon.servicebus.windows.net/;SharedAccessKeyName=SendOnly;SharedAccessKey=xxx|contact-master-matching
        /// </summary>
        public ServiceBusPublisher(string secureConfig, ITracingService tracing)
        {
            _tracing = tracing ?? throw new ArgumentNullException(nameof(tracing));

            if (string.IsNullOrWhiteSpace(secureConfig))
                throw new ArgumentException("Secure Configuration vacia. Configurar connection string y queue name.");

            var parts = secureConfig.Split('|');
            if (parts.Length != 2)
                throw new ArgumentException(
                    "Secure Configuration con formato invalido. Esperado: '{connectionString}|{queueName}'");

            var connectionString = parts[0].Trim();
            _queueName = parts[1].Trim();

            ParseConnectionString(connectionString, out _endpoint, out _keyName, out _keyValue);
        }

        /// <summary>
        /// Publica un mensaje JSON en Service Bus con el SessionId indicado.
        /// El caller es responsable de serializar el payload a JSON.
        /// </summary>
        public async Task PublishAsync(string jsonPayload, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(jsonPayload))  throw new ArgumentNullException(nameof(jsonPayload));
            if (string.IsNullOrWhiteSpace(sessionId))    throw new ArgumentNullException(nameof(sessionId));

            // URL de la REST API de Service Bus para enviar un mensaje
            var url = $"{_endpoint}/{_queueName}/messages";

            // SAS token valido por 5 minutos (mas que suficiente para el round trip)
            var sasToken = GenerateSasToken(url, _keyName, _keyValue, expirySeconds: 300);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("SharedAccessSignature", sasToken.Substring("SharedAccessSignature ".Length));

                // BrokerProperties: define el SessionId para la queue con sessions
                request.Headers.Add("BrokerProperties",
                    $"{{\"SessionId\":\"{EscapeJson(sessionId)}\"}}");

                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _tracing.Trace(
                    "[ServiceBusPublisher] Publicando mensaje a {0}/{1} | SessionId={2} | PayloadSize={3}b",
                    _endpoint, _queueName, sessionId, jsonPayload.Length);

                using (var response = await _httpClient.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException(
                            $"Service Bus respondio {(int)response.StatusCode}: {body}");
                    }
                }
            }

            _tracing.Trace("[ServiceBusPublisher] Mensaje publicado correctamente.");
        }

        // ----------------------------------------------------------------
        // SAS Token
        // ----------------------------------------------------------------

        /// <summary>
        /// Genera un SAS token para autenticar contra la REST API de Service Bus.
        /// Algoritmo: HMACSHA256 sobre "{resourceUri}\n{expiry}" usando el SharedAccessKey.
        /// </summary>
        private static string GenerateSasToken(
            string resourceUri, string keyName, string keyValue, int expirySeconds)
        {
            var expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
            var encodedUri = Uri.EscapeDataString(resourceUri);
            var stringToSign = encodedUri + "\n" + expiry;

            string signature;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyValue)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
                signature = Uri.EscapeDataString(Convert.ToBase64String(hash));
            }

            return $"SharedAccessSignature sr={encodedUri}&sig={signature}&se={expiry}&skn={keyName}";
        }

        // ----------------------------------------------------------------
        // Connection string parser
        // ----------------------------------------------------------------

        private static void ParseConnectionString(
            string connectionString, out string endpoint, out string keyName, out string keyValue)
        {
            endpoint = null;
            keyName  = null;
            keyValue = null;

            foreach (var segment in connectionString.Split(';'))
            {
                var idx = segment.IndexOf('=');
                if (idx < 0) continue;

                var key = segment.Substring(0, idx).Trim();
                var val = segment.Substring(idx + 1).Trim();

                switch (key.ToLowerInvariant())
                {
                    case "endpoint":
                        // sb://namespace.servicebus.windows.net/ → https://namespace.servicebus.windows.net
                        endpoint = "https://" + new Uri(val).Host;
                        break;
                    case "sharedaccesskeyname":
                        keyName = val;
                        break;
                    case "sharedaccesskey":
                        keyValue = val;
                        break;
                }
            }

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(keyName) || string.IsNullOrEmpty(keyValue))
                throw new ArgumentException(
                    "Connection string invalida. Debe contener Endpoint, SharedAccessKeyName y SharedAccessKey.");
        }

        private static string EscapeJson(string value) =>
            value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
    }
}
