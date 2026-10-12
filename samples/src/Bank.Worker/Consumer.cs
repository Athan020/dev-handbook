using System.Diagnostics;
using Bank.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bank.Worker;

/// <summary>
/// Consumes one queue and does one job, chosen by Worker:Role:
///   follow-up (the default): stage 1's single queue; email and fraud score together.
///   email / fraud: stage 2's separate queues, one per service, fed by the transfers exchange.
///   rewards: stage 3's rewards service, which records points in the database.
///   history: reads the transfers stream from Worker:StreamOffset ("first", "last", "next").
/// A message is acknowledged only after its work succeeds, so a crash mid-way means RabbitMQ
/// delivers it again (at-least-once delivery, stage 3). Worker:Prefetch caps how many
/// unacknowledged messages it holds; Worker:Concurrency is how many it works on at once (stage 7).
/// Worker:Idempotent makes email and rewards safe to repeat (stage 3). Worker:CrashChance
/// simulates the process dying after the work but before the acknowledgement (stage 3).
/// Worker:Retries chooses what happens to a message that fails (stage 6): Requeue (the
/// default) puts it straight back; Backoff retries it after a delay, and dead-letters it after
/// three attempts, or at once if it's malformed. Role "replay" moves dead letters back.
/// </summary>
public sealed class Consumer(
    IConfiguration config, IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<Consumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var role = config["Worker:Role"] ?? "follow-up";
        var prefetch = config.GetValue<ushort>("Worker:Prefetch", 10);
        var concurrency = config.GetValue<ushort>("Worker:Concurrency", 1);
        var idempotent = config.GetValue("Worker:Idempotent", false);
        var crashChance = config.GetValue("Worker:CrashChance", 0.0);
        var backoff = config["Worker:Retries"] == "Backoff";

        await using var connection = await Rabbit.Factory(config, $"bank-worker-{role}").CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(false, false, consumerDispatchConcurrency: concurrency), stoppingToken);
        await Rabbit.DeclareFollowUpQueueAsync(channel, stoppingToken);
        await Rabbit.DeclareTransferTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, stoppingToken);

        if (role == "replay")
        {
            await ReplayDeadLettersAsync(channel, config["Worker:Queue"] ?? Rabbit.EmailQueue, stoppingToken);
            lifetime.StopApplication(); // a one-off job: exit when done
            return;
        }

        // What each role does with one transfer: (services, transfer, message id) => work.
        var (queue, work) = role switch
        {
            "email" => (Rabbit.EmailQueue, (Func<IServiceProvider, TransferCompleted, string, Task>)((s, t, id) =>
                s.GetRequiredService<FollowUp>().SendEmailAsync(t, stoppingToken, idempotent ? id : null))),
            "fraud" => (Rabbit.FraudQueue, (s, t, _) => s.GetRequiredService<FollowUp>().ScoreForFraudAsync(t, stoppingToken)),
            "rewards" => (Rabbit.RewardsQueue, (s, t, id) => idempotent
                ? s.GetRequiredService<Rewards>().AwardOnceAsync(t, id, stoppingToken)
                : s.GetRequiredService<Rewards>().AwardAsync(t, stoppingToken)),
            "history" => (Rabbit.TransfersStream, (_, _, _) => Task.CompletedTask),
            _ => (Rabbit.FollowUpQueue, (s, t, _) => s.GetRequiredService<FollowUp>().RunAsync(t, stoppingToken)),
        };

        if (backoff) await Retries.DeclareAsync(channel, queue, stoppingToken);

        var handled = 0;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            TransferCompleted? transfer = null;
            try
            {
                transfer = Rabbit.Deserialize<TransferCompleted>(delivery.Body);
                await using var scope = scopes.CreateAsyncScope();
                await work(scope.ServiceProvider, transfer, delivery.BasicProperties.MessageId ?? transfer.TransferId.ToString());

                // #region simulated-crash
                // Lab only: die after doing the work but before acknowledging it, as a real crash,
                // deployment or lost connection can. RabbitMQ will deliver the message again.
                if (crashChance > 0 && Random.Shared.NextDouble() < crashChance)
                {
                    log.LogWarning("Simulated crash after handling transfer {TransferId}, before acknowledging it", transfer.TransferId);
                    Process.GetCurrentProcess().Kill();
                }
                // #endregion simulated-crash

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                var count = Interlocked.Increment(ref handled);
                if (role == "history")
                {
                    if (count % 100 == 0) log.LogInformation("Read {Count} events from the stream so far", count);
                }
                else
                {
                    log.LogInformation("{Role}: handled transfer {TransferId}{Redelivered}", role, transfer.TransferId,
                        delivery.Redelivered ? " (redelivered)" : "");
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var id = transfer?.TransferId.ToString() ?? delivery.BasicProperties.MessageId ?? "(unreadable)";
                if (backoff)
                {
                    // Stage 6: move it to a retry queue or the dead-letter queue, then let it go.
                    await Retries.HandleFailureAsync(channel, queue, delivery.Body, delivery.BasicProperties, e, stoppingToken);
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                    log.LogWarning("{Role}: message {Id} failed on attempt {Attempt} ({Error}); moved on", role, id,
                        Retries.AttemptOf(delivery.BasicProperties) + 1, e.Message);
                }
                else
                {
                    // #region requeue
                    // Put it back on the queue to try again, straight away. Fine for a blip;
                    // for a message that can never succeed, an endless loop.
                    log.LogWarning("{Role}: message {Id} failed ({Error}); requeueing", role, id, e.Message);
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
                    // #endregion requeue
                }
            }
        };

        // Streams need a starting offset; queues don't take one.
        var arguments = role == "history"
            ? new Dictionary<string, object?> { ["x-stream-offset"] = config["Worker:StreamOffset"] ?? "first" }
            : null;
        await channel.BasicConsumeAsync(queue, autoAck: false, consumerTag: "", noLocal: false, exclusive: false,
            arguments, consumer, stoppingToken);
        log.LogInformation("{Role}: consuming {Queue} (prefetch {Prefetch}, concurrency {Concurrency})", role, queue, prefetch, concurrency);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        if (role == "history") log.LogInformation("Read {Count} events from the stream in total", handled);
    }

    // #region replay
    // Move everything in a queue's dead-letter queue back onto the queue, with the attempt count
    // reset: for after the cause is fixed. Then stop.
    private async Task ReplayDeadLettersAsync(IChannel channel, string queue, CancellationToken ct)
    {
        var moved = 0;
        while (await channel.BasicGetAsync(Retries.DeadLetterQueue(queue), autoAck: false, ct) is { } message)
        {
            var properties = new BasicProperties
            {
                MessageId = message.BasicProperties.MessageId,
                Type = message.BasicProperties.Type,
                ContentType = message.BasicProperties.ContentType,
                Persistent = true,
            };
            await channel.BasicPublishAsync("", queue, mandatory: true, properties, message.Body, ct);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, ct);
            moved++;
        }
        log.LogInformation("Replayed {Count} dead-lettered messages onto {Queue}", moved, queue);
    }
    // #endregion replay
}
