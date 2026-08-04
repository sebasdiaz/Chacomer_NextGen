using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Messaging
{
    /// <summary>
    /// Publica mensajes en el backbone asincronico de la EiP.
    /// </summary>
    public interface IEipMessagePublisher
    {
        /// <summary>
        /// Envia el envelope a la cola indicada. Si el envelope trae
        /// <see cref="EipMessage{T}.PartitionKey"/>, viaja como SessionId — que es lo
        /// que ordena los mensajes de una misma clave en las colas con sessions.
        /// </summary>
        Task PublishAsync<TPayload>(
            string queueName,
            EipMessage<TPayload> message,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementacion sobre Azure Service Bus. Los senders se cachean por cola: crearlos
    /// es caro (abren su propio link AMQP) y son thread-safe.
    /// </summary>
    public sealed class EipServiceBusPublisher : IEipMessagePublisher, IAsyncDisposable
    {
        private readonly ServiceBusClient _client;
        private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<EipServiceBusPublisher> _logger;

        public EipServiceBusPublisher(ServiceBusClient client, ILogger<EipServiceBusPublisher> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PublishAsync<TPayload>(
            string queueName,
            EipMessage<TPayload> message,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
            ArgumentNullException.ThrowIfNull(message);

            var body = JsonSerializer.Serialize(message, EipMessageDefaults.SerializerOptions);

            var serviceBusMessage = new ServiceBusMessage(body)
            {
                MessageId     = message.MessageId,
                CorrelationId = message.CorrelationId,
                ContentType   = "application/json",
                Subject       = $"{message.EntityType}.{message.Operation}"
            };

            if (!string.IsNullOrWhiteSpace(message.PartitionKey))
                serviceBusMessage.SessionId = message.PartitionKey;

            var sender = _senders.GetOrAdd(queueName, _client.CreateSender);

            await sender.SendMessageAsync(serviceBusMessage, cancellationToken);

            _logger.LogInformation(
                "[EipServiceBusPublisher] Publicado en '{Queue}'. MessageId={MessageId} | " +
                "CorrelationId={CorrelationId} | {EntityType}.{Operation} | SessionId={SessionId}",
                queueName, message.MessageId, message.CorrelationId,
                message.EntityType, message.Operation, message.PartitionKey ?? "(sin session)");
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var sender in _senders.Values)
                await sender.DisposeAsync();

            await _client.DisposeAsync();
        }
    }
}
