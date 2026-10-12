using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Bank.Messaging;

/// <summary>
/// B2 stage 6: what a consumer does with a message that failed.
/// Retry queues hold a message for a fixed delay, then RabbitMQ dead-letters it back to the
/// main queue: a delayed retry, without a scheduler. Every message in one retry queue has the
/// same delay, so none waits behind a longer one.
/// </summary>
public static class Retries
{
    public static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    public const string AttemptHeader = "x-attempt";
    public const string ErrorHeader = "x-error";

    public static string RetryQueue(string queue, TimeSpan delay) => $"{queue}.retry.{(int)delay.TotalSeconds}s";
    public static string DeadLetterQueue(string queue) => $"{queue}.dead";

    // #region retry-topology
    public static async Task DeclareAsync(IChannel channel, string queue, CancellationToken ct)
    {
        foreach (var delay in Delays)
        {
            await channel.QueueDeclareAsync(RetryQueue(queue, delay), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)delay.TotalMilliseconds,  // hold each message this long...
                    ["x-dead-letter-exchange"] = "",                    // ...then send it back,
                    ["x-dead-letter-routing-key"] = queue,              // to the main queue
                }, cancellationToken: ct);
        }
        // Where messages go when retrying is pointless or has run out: kept for a person to inspect.
        await channel.QueueDeclareAsync(DeadLetterQueue(queue), durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
    }
    // #endregion retry-topology

    public static int AttemptOf(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(AttemptHeader, out var value) == true ? Convert.ToInt32(value) : 0;

    // #region handle-failure
    /// <summary>
    /// A malformed message will fail every time, so it goes straight to the dead-letter queue.
    /// Anything else is retried after a growing delay, up to three times, then dead-lettered.
    /// </summary>
    public static async Task HandleFailureAsync(IChannel channel, string queue, ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties original, Exception error, CancellationToken ct)
    {
        var attempt = AttemptOf(original);
        var permanent = error is JsonException;
        var target = permanent || attempt >= Delays.Length
            ? DeadLetterQueue(queue)
            : RetryQueue(queue, Delays[attempt]);

        var properties = new BasicProperties
        {
            MessageId = original.MessageId,
            Type = original.Type,
            ContentType = original.ContentType,
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                [AttemptHeader] = attempt + 1,
                [ErrorHeader] = Encoding.UTF8.GetBytes(error.Message),
            },
        };
        await channel.BasicPublishAsync("", target, mandatory: true, properties, body, ct);
    }
    // #endregion handle-failure
}
