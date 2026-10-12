using System.Net.Http.Json;
using System.Text.Json;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;

namespace Bank.Api.Tests;

/// <summary>B2 stages 5 and 6.</summary>
public sealed class OrderingAndRetryTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_an_account_and_transferring_number_its_events_in_order()
    {
        await using var app = fixture.App("Outbox");
        var client = app.CreateClient();

        var opened = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("Sequence test"), Ct);
        var account = (await opened.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/transfers", new TransferRequest(account.Id, 1, 5m, $"Sequence {i}"), Ct)).EnsureSuccessStatusCode();
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        var events = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.Exchange == Rabbit.AccountsExchange)
            .OrderBy(o => o.Position)
            .ToListAsync(Ct);
        var sequences = events
            .Select(o => JsonDocument.Parse(o.Payload).RootElement)
            .Where(e => e.GetProperty("accountId").GetInt32() == account.Id)
            .Select(e => e.GetProperty("sequence").GetInt32())
            .ToList();

        Assert.Equal([1, 2, 3, 4], sequences);
        Assert.All(events.Where(o => o.Payload.Contains($"\"accountId\":{account.Id},")),
            o => Assert.Equal(Rabbit.AccountRoutingKey(account.Id, o.Type == nameof(AccountOpened) ? "opened" : "entry-posted"), o.RoutingKey));
    }

    [Fact]
    public async Task Statements_apply_events_in_order_and_record_disorder()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var statements = new Statements(dataSource);
        var now = DateTimeOffset.UtcNow;

        // In order: no problems.
        await statements.OpenAsync(new AccountOpened(9001, "In order", 1, now), Ct);
        await statements.PostAsync(new AccountEntryPosted(9001, Guid.NewGuid(), 100m, 2, now), Ct);
        await statements.PostAsync(new AccountEntryPosted(9001, Guid.NewGuid(), -30m, 3, now), Ct);

        // Out of order: event 3 before event 2. Event 2 then looks old, and is skipped.
        await statements.OpenAsync(new AccountOpened(9002, "Out of order", 1, now), Ct);
        await statements.PostAsync(new AccountEntryPosted(9002, Guid.NewGuid(), -30m, 3, now), Ct);
        await statements.PostAsync(new AccountEntryPosted(9002, Guid.NewGuid(), 100m, 2, now), Ct);

        await using var read = dataSource.CreateCommand(
            "SELECT account_id, balance, (SELECT count(*) FROM statement_problems p WHERE p.account_id = s.account_id) " +
            "FROM statement_accounts s WHERE account_id IN (9001, 9002) ORDER BY 1");
        await using var reader = await read.ExecuteReaderAsync(Ct);
        var rows = new List<(int Account, decimal Balance, long Problems)>();
        while (await reader.ReadAsync(Ct)) rows.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetInt64(2)));

        Assert.Equal((9001, 70m, 0L), rows[0]);
        Assert.Equal((9002, -30m, 1L), rows[1]); // the R100 credit was lost
    }

    [Fact]
    public async Task Failed_messages_go_to_retry_queues_then_the_dead_letter_queue_and_retries_come_back()
    {
        const string queue = "retry-test";
        var factory = new ConnectionFactory { HostName = fixture.RabbitHost, Port = fixture.RabbitPort, UserName = "bank", Password = "bank" };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: Ct);
        await Retries.DeclareAsync(channel, queue, Ct);
        var body = "{}"u8.ToArray();

        async Task<uint> CountAsync(string name) => (await channel.QueueDeclarePassiveAsync(name, Ct)).MessageCount;
        BasicProperties WithAttempt(int attempt) => new() { MessageId = "m", Headers = new Dictionary<string, object?> { [Retries.AttemptHeader] = attempt } };

        // A message that can't be read is parked straight away.
        await Retries.HandleFailureAsync(channel, queue, body, new BasicProperties { MessageId = "m" }, new JsonException("bad"), Ct);
        // A transient failure on the first attempt waits in the 1 s retry queue...
        await Retries.HandleFailureAsync(channel, queue, body, new BasicProperties { MessageId = "m" }, new HttpRequestException("503"), Ct);
        // ...and after the last retry, it's parked.
        await Retries.HandleFailureAsync(channel, queue, body, WithAttempt(Retries.Delays.Length), new HttpRequestException("503"), Ct);

        await Task.Delay(200, Ct);
        Assert.Equal(2u, await CountAsync(Retries.DeadLetterQueue(queue)));
        Assert.Equal(1u, await CountAsync(Retries.RetryQueue(queue, Retries.Delays[0])));

        // The retry queue hands it back to the main queue after its delay, with the attempt counted.
        await Task.Delay(Retries.Delays[0] + TimeSpan.FromSeconds(1), Ct);
        var returned = await channel.BasicGetAsync(queue, autoAck: true, Ct);
        Assert.NotNull(returned);
        Assert.Equal(1, Retries.AttemptOf(returned.BasicProperties));
    }
}
