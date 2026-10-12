using Bank.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bank.Worker;

/// <summary>
/// B2 stage 5: the statements service (Worker:Role=statements). Worker:Topology chooses:
///   Shared (the default): one queue for all account events, handled Worker:Concurrency at a time.
///   Partitioned: one queue per partition, each handled one message at a time. Several workers
///   can run; RabbitMQ makes one of them the single active consumer of each partition.
/// </summary>
public sealed class StatementsConsumer(IConfiguration config, Statements statements, ILogger<StatementsConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config["Worker:Role"] != "statements") return;

        var partitioned = config["Worker:Topology"] == "Partitioned";
        var concurrency = partitioned ? (ushort)1 : config.GetValue<ushort>("Worker:Concurrency", 1);
        await using var connection = await Rabbit.Factory(config, "bank-worker-statements").CreateConnectionAsync(stoppingToken);

        var queues = partitioned
            ? Enumerable.Range(0, Rabbit.StatementPartitions).Select(Rabbit.StatementPartitionQueue).ToArray()
            : [Rabbit.StatementsSharedQueue];

        var channels = new List<IChannel>();
        foreach (var queue in queues)
        {
            // A channel per queue, so partitions are processed independently of each other.
            var channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false, consumerDispatchConcurrency: concurrency), stoppingToken);
            channels.Add(channel);
            await Rabbit.DeclareAccountTopologyAsync(channel, stoppingToken);
            await channel.BasicQosAsync(0, config.GetValue<ushort>("Worker:Prefetch", 10), false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                switch (delivery.BasicProperties.Type)
                {
                    case nameof(AccountOpened):
                        await statements.OpenAsync(Rabbit.Deserialize<AccountOpened>(delivery.Body), stoppingToken);
                        break;
                    case nameof(AccountEntryPosted):
                        await statements.PostAsync(Rabbit.Deserialize<AccountEntryPosted>(delivery.Body), stoppingToken);
                        break;
                }
                await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
            };
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        }

        log.LogInformation("statements: consuming {Queues} (concurrency {Concurrency} each)", string.Join(", ", queues), concurrency);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        foreach (var channel in channels) await channel.DisposeAsync();
    }
}
