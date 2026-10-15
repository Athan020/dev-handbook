using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Bank.Api.Data;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public sealed record TransferRequest(int FromAccountId, int ToAccountId, decimal Amount, string Reference);

public sealed record OpenAccountRequest(string HolderName);

/// <summary>
/// Journey B2. A transfer moves money between two accounts, then follow-up work happens:
/// an email to the customer and a fraud score. Lab:Transfers chooses where that follow-up
/// work runs, so you can compare:
///   Inline (the default): inside the request, before responding.
///   Background: handed to an in-process queue that a background service drains.
///   Queue: published to RabbitMQ after the transfer commits; Bank.Worker does the work.
///   Event: a "transfer completed" event published to an exchange; each service that cares
///   has its own queue (B2 stage 2).
///   Outbox: the event is saved in the same transaction as the transfer, and OutboxRelay
///   publishes it (B2 stage 4).
/// Lab:CrashAfterCommit (0 to 1) kills the API after the transfer commits and before anything
/// is published, to show what that does to each version.
/// Journey B3 adds Lab:Funds, Lab:Isolation and Lab:Retries: see Funds.
/// </summary>
public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers", CreateTransfer);
        app.MapPost("/accounts", OpenAccount);
        return app;
    }

    private static async Task<IResult> CreateTransfer(
        TransferRequest request, BankDb db, IConfiguration config, IServiceProvider services, HttpContext http, CancellationToken ct)
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

        var outbox = config["Lab:Transfers"] == "Outbox";

        // B3 stage 3: the client's Idempotency-Key header, if Lab:Idempotency uses it.
        var key = http.Request.Headers[Idempotency.Header].ToString();
        var keyMode = key.Length is > 0 and <= 100 ? config["Lab:Idempotency"] : null;
        var requestHash = Idempotency.Hash(request);
        if (keyMode == "CheckFirst" && await Idempotency.FindAsync(db, request.FromAccountId, key, ct) is { } previous)
        {
            return previous.RequestHash == requestHash
                ? Replay(http, previous.StatusCode!.Value, previous.Response!)
                : KeyMismatch();
        }
        var claim = keyMode == "Keys" ? (key, requestHash) : ((string, string)?)null;

        // B4: ask the fraud service before moving any money, either here, before the database
        // transaction, or (Lab:FraudGate=InTransaction) inside it, holding a connection while it waits.
        var fraud = config["Lab:FraudGate"] is "BeforeTransaction" or "InTransaction" ? services.GetRequiredService<FraudClient>() : null;
        if (fraud is not null && config["Lab:FraudGate"] == "BeforeTransaction")
        {
            var outcome = await fraud.DecideAsync(request.FromAccountId, request.ToAccountId, request.Amount, ct);
            if (outcome.Decision != FraudDecision.Approved)
            {
                return FraudRefusal(outcome);
            }
        }
        var fraudInTransaction = config["Lab:FraudGate"] == "InTransaction" ? fraud : null;

        // #region retry-loop
        // B3: a transaction that lost a race (a serialization failure or a deadlock) is rolled
        // back. Run the whole transaction again, up to Lab:Retries times, then give up with 409.
        Recorded recorded;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                recorded = await RecordTransferAsync(db, request, outbox, config, claim, fraudInTransaction, ct);
                break;
            }
            catch (Exception e) when (Funds.IsRetryable(e))
            {
                db.ChangeTracker.Clear(); // forget the rows the failed attempt added
                if (attempt >= config.GetValue("Lab:Retries", 0))
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The transfer conflicted with another. Try again.");
                }
            }
        }
        // #endregion retry-loop

        if (recorded.Fraud is { } refused)
        {
            return FraudRefusal(refused);
        }

        switch (recorded.Previous)
        {
            case Idempotency.Claim.Replay replay:
                return Replay(http, replay.StatusCode, replay.Response);
            case Idempotency.Claim.Mismatch:
                return KeyMismatch();
        }

        if (recorded.Transfer is not { } transfer)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Insufficient funds.");
        }

        // Lab only: die here, after the commit and before any publish, as a crash or deployment can.
        if (Random.Shared.NextDouble() < config.GetValue("Lab:CrashAfterCommit", 0.0))
        {
            Process.GetCurrentProcess().Kill();
        }

        if (keyMode == "CheckFirst")
        {
            await Idempotency.RecordAfterwardsAsync(db, request.FromAccountId, key, requestHash, StatusCodes.Status201Created, transfer, ct);
        }

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

            case "Outbox":
                // Nothing to publish here: the event was saved with the transfer, and the relay
                // will publish it, even if this process dies now.
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

    private static IResult Replay(HttpContext http, int statusCode, string response)
    {
        http.Response.Headers[Idempotency.ReplayedHeader] = "true";
        return Results.Content(response, "application/json", statusCode: statusCode);
    }

    private static IResult KeyMismatch() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "This Idempotency-Key was already used with a different request.");

    // #region fraud-refusal
    // Declined means the fraud service said no. Unavailable means we couldn't get an answer:
    // failing closed, the transfer isn't made, and the customer is asked to try again shortly.
    private static IResult FraudRefusal(FraudOutcome outcome) => outcome.Decision == FraudDecision.Declined
        ? Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Declined by fraud checks.")
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Fraud checks are unavailable. Try again shortly.", detail: outcome.Detail);
    // #endregion fraud-refusal

    private sealed record Recorded(TransferCompleted? Transfer, Idempotency.Claim? Previous = null, FraudOutcome? Fraud = null);

    /// <summary>
    /// B2 stage 5: opens an account and saves an AccountOpened event in the outbox, in one
    /// transaction. The relay publishes it when Lab:Transfers=Outbox.
    /// </summary>
    private static async Task<IResult> OpenAccount(OpenAccountRequest request, BankDb db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.HolderName) || request.HolderName.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["holderName"] = ["holderName must be 1 to 200 characters."],
            });
        }

        var account = new Account { HolderName = request.HolderName, Currency = "ZAR", OpenedAt = DateTimeOffset.UtcNow, Version = 1 };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct); // assigns the id

        db.OutboxMessages.Add(Outbox.Message(
            Rabbit.AccountsExchange, Rabbit.AccountRoutingKey(account.Id, "opened"), Guid.CreateVersion7(),
            new AccountOpened(account.Id, account.HolderName, 1, account.OpenedAt)));
        await db.SaveChangesAsync(ct);
        // (Two saves, so a crash between them could lose the event. The lab keeps it simple;
        // in a real service, wrap both in one transaction, as the transfer below does.)

        return Results.Created($"/accounts/{account.Id}", new AccountResponse(account.Id, account.HolderName, account.Currency, account.OpenedAt));
    }

    /// <summary>
    /// Records the debit and the credit in one database transaction. Journey B3 adds the checks
    /// that make it safe to move money (see Funds): returns null if the account can't afford it.
    /// </summary>
    private static async Task<Recorded> RecordTransferAsync(
        BankDb db, TransferRequest request, bool outbox, IConfiguration config, (string Key, string Hash)? claim,
        FraudClient? fraudInTransaction, CancellationToken ct)
    {
        await using var dbTransaction = await db.Database.BeginTransactionAsync(Funds.Isolation(config), ct);

        // #region fraud-in-transaction
        // B4 stage 1's starting point: the fraud check inside the transaction. The transaction
        // holds a database connection for as long as the fraud service takes to answer.
        if (fraudInTransaction is not null)
        {
            var outcome = await fraudInTransaction.DecideAsync(request.FromAccountId, request.ToAccountId, request.Amount, ct);
            if (outcome.Decision != FraudDecision.Approved)
            {
                return new Recorded(null, Fraud: outcome);
            }
        }
        // #endregion fraud-in-transaction

        // #region claim-in-transaction
        // B3 stage 3 (Lab:Idempotency=Keys): claim the key first, in this transaction. A key
        // that's already been used means "this payment was already made": answer as before.
        if (claim is var (key, hash) && await Idempotency.ClaimAsync(db, request.FromAccountId, key, hash, ct) is var claimed
            && claimed is not Idempotency.Claim.New)
        {
            return new Recorded(null, claimed);
        }
        // #endregion claim-in-transaction

        if (config["Lab:Funds"] == "CheckSum" && !await Funds.HasFundsAsync(db, request.FromAccountId, request.Amount, ct))
        {
            return new Recorded(null); // nothing written; disposing the transaction rolls it back
        }

        // B3 stage 2: Lab:LockLate=true writes everything else first and changes the balances
        // last, so the account rows stay locked only from that point until the commit.
        var lockLate = config.GetValue("Lab:LockLate", false);
        if (!lockLate && !await Funds.MoveAsync(db, request, config, ct))
        {
            return new Recorded(null); // the stored balance can't cover it; rolling back undoes any change made
        }

        var now = DateTimeOffset.UtcNow;
        var transfer = new TransferCompleted(
            Guid.CreateVersion7(), request.FromAccountId, request.ToAccountId, request.Amount, request.Reference, now);
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

        // #region outbox-write
        // B2 stage 4: the event is a row in the same transaction as the transfer. Both commit,
        // or neither does. The relay publishes it later.
        if (outbox)
        {
            db.OutboxMessages.Add(Outbox.Message(
                Rabbit.TransfersExchange, Rabbit.TransferCompletedKey, transfer.TransferId, transfer));
        }
        // #endregion outbox-write

        if (outbox)
        {
            // #region account-sequence
            // B2 stage 5: each account's events are numbered. Taking the next number locks the
            // account's row until this transaction commits, so concurrent transfers can't share a
            // number. Lock the lower id first, so two transfers between the same accounts in
            // opposite directions can't deadlock.
            foreach (var (accountId, amount) in new[] { (request.FromAccountId, -request.Amount), (request.ToAccountId, request.Amount) }
                         .OrderBy(e => e.Item1))
            {
                var sequence = await db.Database
                    .SqlQuery<int>($"UPDATE accounts SET version = version + 1 WHERE id = {accountId} RETURNING version AS \"Value\"")
                    .ToListAsync(ct);
                db.OutboxMessages.Add(Outbox.Message(
                    Rabbit.AccountsExchange, Rabbit.AccountRoutingKey(accountId, "entry-posted"), Guid.CreateVersion7(),
                    new AccountEntryPosted(accountId, transfer.TransferId, amount, sequence.Single(), now)));
            }
            // #endregion account-sequence
        }

        await db.SaveChangesAsync(ct);

        // #region lock-late
        // Everything else is written; now lock and change the balances, and commit straight away.
        if (lockLate && !await Funds.MoveAsync(db, request, config, ct))
        {
            return new Recorded(null); // rolling back also removes the rows written above
        }
        // #endregion lock-late

        if (claim is var (claimedKey, _))
        {
            // The response goes in with the key, so a retry gets exactly this answer.
            await Idempotency.CompleteAsync(db, request.FromAccountId, claimedKey, StatusCodes.Status201Created, transfer, ct);
        }

        await dbTransaction.CommitAsync(ct);
        return new Recorded(transfer);
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
public sealed class BackgroundFollowUpService(
    BackgroundFollowUpQueue queue, IServiceScopeFactory scopes, IConfiguration config, ILogger<BackgroundFollowUpService> log)
    : BackgroundService
{
    // B4 stage 4: Lab:FollowUpConcurrency workers drain the queue at once (default 1).
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, config.GetValue("Lab:FollowUpConcurrency", 1)).Select(_ => DrainAsync(stoppingToken)));

    private async Task DrainAsync(CancellationToken stoppingToken)
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
