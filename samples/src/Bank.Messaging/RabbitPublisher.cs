using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Bank.Messaging;

/// <summary>
/// Publishes messages with publisher confirms: PublishAsync only returns once the broker has
/// taken responsibility for the message, and throws if it refuses. One channel, used by one
/// publish at a time; enough for the labs. The connection opens on first use.
/// </summary>
public sealed class RabbitPublisher(IConfiguration config) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public Task PublishAsync<T>(string exchange, string routingKey, T message, string messageId, CancellationToken ct) =>
        PublishRawAsync(exchange, routingKey, typeof(T).Name, Rabbit.Serialize(message), messageId, ct);

    /// <summary>Publishes an already-serialised message, such as a row from the outbox.</summary>
    public async Task PublishRawAsync(string exchange, string routingKey, string type, byte[] body, string messageId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var channel = await ChannelAsync(ct);
            var properties = new BasicProperties
            {
                MessageId = messageId,
                Type = type,
                ContentType = "application/json",
                Persistent = true, // written to disk, so it survives a broker restart
            };
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> ChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        _connection ??= await Rabbit.Factory(config, "bank-api").CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await Rabbit.DeclareFollowUpQueueAsync(_channel, ct);
        await Rabbit.DeclareTransferTopologyAsync(_channel, ct);
        await Rabbit.DeclareAccountTopologyAsync(_channel, ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _lock.Dispose();
    }
}
