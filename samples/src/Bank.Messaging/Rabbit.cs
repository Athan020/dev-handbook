using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Bank.Messaging;

/// <summary>Connecting to RabbitMQ, and the names the labs use. Settings come from RabbitMQ:*.</summary>
public static class Rabbit
{
    /// <summary>Stage 1: one durable queue of follow-up work.</summary>
    public const string FollowUpQueue = "transfer-follow-up";

    public static ConnectionFactory Factory(IConfiguration config, string clientName) => new()
    {
        HostName = config["RabbitMQ:Host"] ?? "localhost",
        Port = config.GetValue("RabbitMQ:Port", 5672),
        UserName = config["RabbitMQ:User"] ?? "bank",
        Password = config["RabbitMQ:Password"] ?? "bank",
        ClientProvidedName = clientName,
    };

    public static Task DeclareFollowUpQueueAsync(IChannel channel, CancellationToken ct) =>
        // Durable: the queue survives a broker restart. (Messages must also be persistent.)
        channel.QueueDeclareAsync(FollowUpQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

    public static byte[] Serialize<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

    public static T Deserialize<T>(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<T>(body.Span, JsonSerializerOptions.Web)
        ?? throw new JsonException($"Empty {typeof(T).Name} message.");
}
