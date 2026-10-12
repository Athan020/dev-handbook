using System.Threading.Channels;
using Bank.Api.Data;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public sealed record TransferRequest(int FromAccountId, int ToAccountId, decimal Amount, string Reference);

/// <summary>
/// Journey B2. A transfer moves money between two accounts, then follow-up work happens:
/// an email to the customer and a fraud score. Lab:Transfers chooses where that follow-up
/// work runs, so you can compare:
///   Inline (the default): inside the request, before responding.
///   Background: handed to an in-process queue that a background service drains.
///   Queue: published to RabbitMQ after the transfer commits; Bank.Worker does the work.
///   Event: a "transfer completed" event published to an exchange; each service that cares
///   has its own queue (B2 stage 2).
/// </summary>
public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers", CreateTransfer);
        return app;
    }

    private static async Task<IResult> CreateTransfer(
        TransferRequest request, BankDb db, IConfiguration config, IServiceProvider services, CancellationToken ct)
    {
        if (request.Amount <= 0 || request.FromAccountId == request.ToAccountId || string.IsNullOrWhiteSpace(request.Reference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transfer"] = ["amount must be positive, the accounts must differ, and a reference is required."],
            });
        }

        var found = await db.Accounts.CountAsync(a => a.Id == request.FromAccountId || a.Id == request.ToAccountId, ct);
        if (found != 2)
        {
            return Results.NotFound();
        }

        var transfer = await RecordTransferAsync(db, request, ct);

        switch (config["Lab:Transfers"])
        {
            case "Background":
                // #region background
                // Hand the work to an in-process queue and respond straight away. Fast, but the
                // queue lives in this process's memory: if the process stops, pending work is lost.
                await services.GetRequiredService<BackgroundFollowUpQueue>().EnqueueAsync(transfer, ct);
                // #endregion background
                break;

            case "Queue":
                // #region queue
                // Publish a message to RabbitMQ once the transfer has committed; a separate worker
                // does the follow-up work. Durable, but see B2 stage 4: if this process dies
                // between the commit above and this publish, the event is never sent.
                await services.GetRequiredService<RabbitPublisher>().PublishAsync(
                    exchange: "", routingKey: Rabbit.FollowUpQueue, transfer, transfer.TransferId.ToString(), ct);
                // #endregion queue
                break;

            case "Event":
                // #region event
                // Publish what happened, not what to do: one event, to an exchange. Each interested
                // service binds its own queue and gets its own copy. The API doesn't know who they are.
                await services.GetRequiredService<RabbitPublisher>().PublishAsync(
                    Rabbit.TransfersExchange, Rabbit.TransferCompletedKey, transfer, transfer.TransferId.ToString(), ct);
                // #endregion event
                break;

            default:
                // #region inline
                // The first version: do the follow-up work inside the request. The customer waits
                // for the email and the fraud score, and if either service fails, the request fails,
                // even though the money has already moved.
                await services.GetRequiredService<FollowUp>().RunAsync(transfer, ct);
                // #endregion inline
                break;
        }

        return Results.Created($"/transfers/{transfer.TransferId}", transfer);
    }

    /// <summary>
    /// Records the debit and the credit in one database transaction (SaveChanges is atomic).
    /// Deliberately simple: no balance check, no idempotency key, no concurrency control.
    /// Journey B3 covers moving money safely.
    /// </summary>
    private static async Task<TransferCompleted> RecordTransferAsync(BankDb db, TransferRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        db.Transactions.AddRange(
            new Transaction
            {
                AccountId = request.FromAccountId, CounterpartyAccountId = request.ToAccountId,
                Amount = -request.Amount, Description = "Transfer", CreatedAt = now,
            },
            new Transaction
            {
                AccountId = request.ToAccountId, CounterpartyAccountId = request.FromAccountId,
                Amount = request.Amount, Description = "Incoming transfer", CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);

        return new TransferCompleted(Guid.CreateVersion7(), request.FromAccountId, request.ToAccountId, request.Amount, request.Reference, now);
    }
}

/// <summary>An in-process queue of follow-up work, held in memory (Lab:Transfers=Background).</summary>
public sealed class BackgroundFollowUpQueue
{
    private readonly Channel<TransferCompleted> _channel = Channel.CreateUnbounded<TransferCompleted>();

    public ValueTask EnqueueAsync(TransferCompleted transfer, CancellationToken ct) => _channel.Writer.WriteAsync(transfer, ct);

    public IAsyncEnumerable<TransferCompleted> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    public int Pending => _channel.Reader.Count;
}

/// <summary>Drains <see cref="BackgroundFollowUpQueue"/>, one transfer at a time.</summary>
public sealed class BackgroundFollowUpService(BackgroundFollowUpQueue queue, IServiceScopeFactory scopes, ILogger<BackgroundFollowUpService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var transfer in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FollowUp>().RunAsync(transfer, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Nowhere to put it back: the failed work is simply lost.
                log.LogError(e, "Follow-up for transfer {TransferId} failed and is lost", transfer.TransferId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (queue.Pending > 0)
        {
            log.LogWarning("Stopping with {Pending} transfers' follow-up work still queued in memory: it will be lost", queue.Pending);
        }
        await base.StopAsync(cancellationToken);
    }
}
