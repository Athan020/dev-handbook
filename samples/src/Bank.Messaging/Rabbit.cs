using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Bank.Sharding;
using RabbitMQ.Client;

namespace Bank.Messaging;

/// <summary>Connecting to RabbitMQ, and the names the labs use. Settings come from RabbitMQ:*.</summary>
public static class Rabbit
{
    /// <summary>Stage 1: one durable queue of follow-up work.</summary>
    public const string FollowUpQueue = "transfer-follow-up";

    // Stage 2: the transfer service publishes one event to an exchange. Each interested service
    // has its own queue bound to it, and so gets its own copy. A stream keeps the history.
    public const string TransfersExchange = "transfers";
    public const string TransferCompletedKey = "transfer.completed";
    public const string EmailQueue = "email.transfer-completed";
    public const string FraudQueue = "fraud.transfer-completed";
    public const string RewardsQueue = "rewards.transfer-completed";
    public const string TransfersStream = "transfers.history";

    // Stage 5: per-account events, for the statements service.
    public const string AccountsExchange = "accounts";
    public const string StatementsSharedQueue = "statements.all";
    public const int StatementPartitions = 4;
    public static string StatementPartitionQueue(int partition) => $"statements.p{partition}";

    // #region partition-key
    /// <summary>
    /// Routing key for an account event: account.{partition}.{event}. The partition comes from a
    /// stable hash of the account id, so every event for one account goes to the same partition.
    /// </summary>
    public static string AccountRoutingKey(int accountId, string eventName) =>
        $"account.{StableHash.Of($"account:{accountId}") % StatementPartitions}.{eventName}";
    // #endregion partition-key

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

    // #region topology
    public static async Task DeclareTransferTopologyAsync(IChannel channel, CancellationToken ct)
    {
        // A topic exchange routes by routing key: "transfer.completed" here, so consumers could
        // also bind to patterns such as "transfer.*" later.
        await channel.ExchangeDeclareAsync(TransfersExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

        // One queue per consuming service. Each gets every event; within a service, several
        // workers on the same queue share the messages (competing consumers).
        foreach (var queue in new[] { EmailQueue, FraudQueue, RewardsQueue })
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(queue, TransfersExchange, TransferCompletedKey, cancellationToken: ct);
        }

        // A stream: an append-only log that consumers read from any offset, without removing
        // messages. Kept for 7 days here, so a new service can replay recent history.
        await channel.QueueDeclareAsync(TransfersStream, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "stream", ["x-max-age"] = "7D" },
            cancellationToken: ct);
        await channel.QueueBindAsync(TransfersStream, TransfersExchange, TransferCompletedKey, cancellationToken: ct);
    }
    // #endregion topology

    // #region account-topology
    public static async Task DeclareAccountTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(AccountsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

        // One shared queue for every account event. Several consumers on it share the work,
        // and lose the order of events for any one account.
        await channel.QueueDeclareAsync(StatementsSharedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(StatementsSharedQueue, AccountsExchange, "account.#", cancellationToken: ct);

        // Or one queue per partition. A single active consumer per queue keeps each account's
        // events in order; different partitions are still processed in parallel.
        for (var p = 0; p < StatementPartitions; p++)
        {
            await channel.QueueDeclareAsync(StatementPartitionQueue(p), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-single-active-consumer"] = true }, cancellationToken: ct);
            await channel.QueueBindAsync(StatementPartitionQueue(p), AccountsExchange, $"account.{p}.*", cancellationToken: ct);
        }
    }
    // #endregion account-topology

    public static byte[] Serialize<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

    public static T Deserialize<T>(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<T>(body.Span, JsonSerializerOptions.Web)
        ?? throw new JsonException($"Empty {typeof(T).Name} message.");
}
