using System.Net.Http.Json;
using System.Threading.Channels;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bank.Api.Tests;

/// <summary>B2 stages 2 to 4, against real Postgres and RabbitMQ.</summary>
public sealed class MessagingTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A temporary queue bound to the transfers exchange, collecting what's published.</summary>
    private async Task<(IConnection, Channel<BasicDeliverEventArgs>)> ListenAsync()
    {
        var factory = new ConnectionFactory { HostName = fixture.RabbitHost, Port = fixture.RabbitPort, UserName = "bank", Password = "bank" };
        var connection = await factory.CreateConnectionAsync(Ct);
        var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Rabbit.DeclareTransferTopologyAsync(channel, Ct);
        var queue = await channel.QueueDeclareAsync(cancellationToken: Ct); // temporary: fine for a test
        await channel.QueueBindAsync(queue.QueueName, Rabbit.TransfersExchange, Rabbit.TransferCompletedKey, cancellationToken: Ct);

        var received = Channel.CreateUnbounded<BasicDeliverEventArgs>();
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await received.Writer.WriteAsync(delivery);
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, Ct);
        return (connection, received);
    }

    private static async Task<BasicDeliverEventArgs> NextAsync(Channel<BasicDeliverEventArgs> received)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return await received.Reader.ReadAsync(timeout.Token);
    }

    [Fact]
    public async Task Event_mode_publishes_transfer_completed_with_the_transfer_id_as_message_id()
    {
        var (connection, received) = await ListenAsync();
        await using var _ = connection;
        await using var app = fixture.App("Event");

        var response = await app.CreateClient().PostAsJsonAsync("/transfers", new TransferRequest(1, 2, 10m, "Event test"), Ct);
        var transfer = await response.Content.ReadFromJsonAsync<TransferCompleted>(Ct);

        var delivery = await NextAsync(received);
        Assert.Equal(transfer!.TransferId.ToString(), delivery.BasicProperties.MessageId);
        Assert.Equal(transfer, Rabbit.Deserialize<TransferCompleted>(delivery.Body));
    }

    [Fact]
    public async Task The_outbox_saves_the_event_with_the_transfer_and_the_relay_publishes_and_marks_it()
    {
        var (connection, received) = await ListenAsync();
        await using var _ = connection;
        await using var app = fixture.App("Outbox");

        var response = await app.CreateClient().PostAsJsonAsync("/transfers", new TransferRequest(3, 4, 25m, "Outbox test"), Ct);
        var transfer = await response.Content.ReadFromJsonAsync<TransferCompleted>(Ct);

        var delivery = await NextAsync(received);
        Assert.Equal(transfer!.TransferId.ToString(), delivery.BasicProperties.MessageId);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        OutboxMessage? row = null;
        for (var i = 0; i < 50 && row?.PublishedAt is null; i++)
        {
            await Task.Delay(100, Ct);
            row = await db.OutboxMessages.AsNoTracking().SingleAsync(o => o.Id == transfer.TransferId, Ct);
        }
        Assert.NotNull(row!.PublishedAt);
    }

    [Fact]
    public async Task Rewards_awarded_naively_repeat_and_awarded_once_do_not()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var rewards = new Rewards(dataSource);
        var naive = new TransferCompleted(Guid.CreateVersion7(), 5, 6, 450m, "Naive", DateTimeOffset.UtcNow);
        var once = new TransferCompleted(Guid.CreateVersion7(), 5, 6, 450m, "Once", DateTimeOffset.UtcNow);

        await rewards.AwardAsync(naive, Ct);
        await rewards.AwardAsync(naive, Ct);
        await rewards.AwardOnceAsync(once, once.TransferId.ToString(), Ct);
        await rewards.AwardOnceAsync(once, once.TransferId.ToString(), Ct);

        await using var count = dataSource.CreateCommand("SELECT transfer_id, count(*), sum(points) FROM reward_points WHERE transfer_id = ANY($1) GROUP BY 1");
        count.Parameters.AddWithValue(new[] { naive.TransferId, once.TransferId });
        var rows = new Dictionary<Guid, (long Count, long Points)>();
        await using var reader = await count.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) rows[reader.GetGuid(0)] = (reader.GetInt64(1), reader.GetInt64(2));

        Assert.Equal((2, 8), rows[naive.TransferId]); // awarded twice: 4 points each time
        Assert.Equal((1, 4), rows[once.TransferId]);
    }
}
