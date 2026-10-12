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
/// </summary>
public sealed class Consumer(IConfiguration config, IServiceScopeFactory scopes, ILogger<Consumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var role = config["Worker:Role"] ?? "follow-up";
        var prefetch = config.GetValue<ushort>("Worker:Prefetch", 10);
        var concurrency = config.GetValue<ushort>("Worker:Concurrency", 1);
        var idempotent = config.GetValue("Worker:Idempotent", false);
        var crashChance = config.GetValue("Worker:CrashChance", 0.0);

        await using var connection = await Rabbit.Factory(config, $"bank-worker-{role}").CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(false, false, consumerDispatchConcurrency: concurrency), stoppingToken);
        await Rabbit.DeclareFollowUpQueueAsync(channel, stoppingToken);
        await Rabbit.DeclareTransferTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, stoppingToken);

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

        var handled = 0;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var transfer = Rabbit.Deserialize<TransferCompleted>(delivery.Body);
            try
            {
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
                // Put it back on the queue to try again. Simple, and dangerous for a message that
                // can never succeed: stage 6 replaces this with retries and a dead-letter queue.
                log.LogWarning("{Role}: transfer {TransferId} failed ({Error}); requeueing", role, transfer.TransferId, e.Message);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
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
}
