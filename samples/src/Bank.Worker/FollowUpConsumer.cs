using Bank.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bank.Worker;

/// <summary>
/// Consumes the follow-up queue. A message is acknowledged only after its work succeeds, so a
/// crash mid-way means RabbitMQ delivers it again (at-least-once delivery, B2 stage 3).
/// Worker:Prefetch caps how many unacknowledged messages it holds; Worker:Concurrency is how
/// many it works on at once (B2 stage 7).
/// </summary>
public sealed class FollowUpConsumer(IConfiguration config, IServiceScopeFactory scopes, ILogger<FollowUpConsumer> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = config.GetValue<ushort>("Worker:Concurrency", 1);
        await using var connection = await Rabbit.Factory(config, "bank-worker").CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(false, false, consumerDispatchConcurrency: concurrency), stoppingToken);

        await Rabbit.DeclareFollowUpQueueAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: config.GetValue<ushort>("Worker:Prefetch", 10), global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var transfer = Rabbit.Deserialize<TransferCompleted>(delivery.Body);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FollowUp>().RunAsync(transfer, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                log.LogInformation("Followed up transfer {TransferId}{Redelivered}", transfer.TransferId,
                    delivery.Redelivered ? " (redelivered)" : "");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Put it back on the queue to try again. Simple, and dangerous for a message that
                // can never succeed: B2 stage 6 replaces this with retries and a dead-letter queue.
                log.LogWarning("Follow-up for transfer {TransferId} failed ({Error}); requeueing", transfer.TransferId, e.Message);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(Rabbit.FollowUpQueue, autoAck: false, consumer, stoppingToken);
        log.LogInformation("Consuming {Queue} (prefetch {Prefetch}, concurrency {Concurrency})",
            Rabbit.FollowUpQueue, config.GetValue<ushort>("Worker:Prefetch", 10), concurrency);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }
}
