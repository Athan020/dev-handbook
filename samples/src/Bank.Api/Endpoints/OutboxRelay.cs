using System.Diagnostics;
using System.Text.Json;
using Bank.Api.Data;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

/// <summary>
/// B2 stage 4: publishes events saved in the outbox table, oldest first, then marks them sent.
/// Runs when Lab:Transfers=Outbox. Lab:RelayCrashChance kills the process after publishing a
/// batch and before marking it, to show that the relay can publish an event twice.
/// </summary>
public sealed class OutboxRelay(IServiceScopeFactory scopes, RabbitPublisher publisher, IConfiguration config, ILogger<OutboxRelay> log)
    : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config["Lab:Transfers"] != "Outbox") return;
        log.LogInformation("Outbox relay running");

        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await PublishBatchAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Nothing was marked as sent, so the next attempt republishes the whole batch.
                log.LogWarning("Outbox relay failed ({Error}); retrying", e.Message);
                published = 0;
            }

            if (published == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    // #region relay
    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Claim the oldest unsent events. SKIP LOCKED lets several relays run side by side
        // without taking the same rows.
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE published_at IS NULL
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (batch.Count == 0) return 0;

        foreach (var message in batch)
        {
            var transfer = JsonSerializer.Deserialize<TransferCompleted>(message.Payload, JsonSerializerOptions.Web)!;
            // The message id is the outbox row's id, the same on every attempt, so consumers
            // can recognize a republished event (stage 3).
            await publisher.PublishAsync(Rabbit.TransfersExchange, Rabbit.TransferCompletedKey, transfer, message.Id.ToString(), ct);
        }

        // Lab only: die after publishing and before marking the batch as sent.
        if (Random.Shared.NextDouble() < config.GetValue("Lab:RelayCrashChance", 0.0))
        {
            log.LogWarning("Simulated relay crash after publishing {Count} events, before marking them sent", batch.Count);
            Process.GetCurrentProcess().Kill();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var message in batch) message.PublishedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }
    // #endregion relay
}
