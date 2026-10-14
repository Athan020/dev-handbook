using System.Net;
using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public sealed record OutgoingPaymentRequest(int FromAccountId, decimal Amount, string ToBank, string ToAccount);

public sealed record OutgoingPaymentResponse(Guid Id, string Status, decimal Amount);

/// <summary>
/// Journey B3 stage 6. Payments to other banks, through a payment network that can't join our
/// database transaction. Lab:Outgoing chooses how the API copes with not knowing the outcome:
///   Naive (the default): debit, call the network, and refund the customer if the call fails.
///   Pending: record the payment as pending with the debit, call the network with the payment's
///   id as its reference, and leave anything unclear to PendingPaymentResolver.
/// Network:TimeoutMs (default 1000) is how long the API waits for the network.
/// </summary>
public static class OutgoingPayments
{
    public static IEndpointRouteBuilder MapOutgoingPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/outgoing-payments", Send);
        app.MapGet("/outgoing-payments/{id:guid}", async (Guid id, BankDb db, CancellationToken ct) =>
            await db.OutgoingPayments.FindAsync([id], ct) is { } p ? Results.Ok(Response(p)) : Results.NotFound());
        app.MapGet("/outgoing-payments/summary", async (int? fromAccountId, BankDb db, CancellationToken ct) =>
            await db.OutgoingPayments.Where(p => fromAccountId == null || p.FromAccountId == fromAccountId).GroupBy(p => p.Status)
                .Select(g => new { status = g.Key, count = g.Count(), amount = g.Sum(p => p.Amount) })
                .ToListAsync(ct));
        return app;
    }

    private static async Task<IResult> Send(
        OutgoingPaymentRequest request, BankDb db, NetworkClient network, IConfiguration config, CancellationToken ct)
    {
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.ToBank) || string.IsNullOrWhiteSpace(request.ToAccount))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["payment"] = ["amount must be positive, and the destination bank and account are required."],
            });
        }

        return config["Lab:Outgoing"] == "Pending"
            ? await SendWithPendingStateAsync(request, db, network, ct)
            : await SendNaivelyAsync(request, db, network, ct);
    }

    // #region naive
    // Debit, call, and on any error give the money back. Correct when the error means "it
    // didn't happen". A timeout doesn't mean that: the payment may have gone through.
    private static async Task<IResult> SendNaivelyAsync(OutgoingPaymentRequest request, BankDb db, NetworkClient network, CancellationToken ct)
    {
        if (!await MoveAsync(db, request.FromAccountId, -request.Amount, $"Payment to {request.ToBank}", ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Insufficient funds.");
        }

        var outcome = await network.SendAsync(Guid.CreateVersion7(), request, ct);
        if (outcome == NetworkOutcome.Settled)
        {
            return Results.Created((string?)null, new { status = "Settled", request.Amount });
        }

        await MoveAsync(db, request.FromAccountId, request.Amount, "Refund: payment failed", ct);
        return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The payment failed, and the money was returned.");
    }
    // #endregion naive

    // #region pending
    // Record the payment as Pending in the same transaction as the debit, then call the network
    // with the payment's id as its reference. A clear answer settles or fails it; anything else
    // leaves it Pending, and the customer is told it's being processed, which is true.
    private static async Task<IResult> SendWithPendingStateAsync(OutgoingPaymentRequest request, BankDb db, NetworkClient network, CancellationToken ct)
    {
        var payment = new OutgoingPayment
        {
            Id = Guid.CreateVersion7(), FromAccountId = request.FromAccountId, Amount = request.Amount,
            ToBank = request.ToBank, ToAccount = request.ToAccount, Status = "Pending", Attempts = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await MoveAsync(db, request.FromAccountId, -request.Amount, $"Payment to {request.ToBank}", ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Insufficient funds.");
            }
            db.OutgoingPayments.Add(payment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var outcome = await network.SendAsync(payment.Id, request, ct);
        if (await ResolveAsync(db, payment, outcome, ct) is { } resolved)
        {
            return resolved == "Settled" ? Results.Created($"/outgoing-payments/{payment.Id}", Response(payment, resolved))
                : Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The network rejected the payment, and the money was returned.");
        }
        return Results.Accepted($"/outgoing-payments/{payment.Id}", Response(payment));
    }
    // #endregion pending

    // #region resolve
    // Move a Pending payment to Settled or Failed (refunding the customer), at most once: the
    // status change only happens if the payment is still Pending, so the request and the
    // resolver can't both settle it, or both refund it. Returns the new status, or null.
    public static async Task<string?> ResolveAsync(BankDb db, OutgoingPayment payment, NetworkOutcome outcome, CancellationToken ct)
    {
        var status = outcome switch
        {
            NetworkOutcome.Settled => "Settled",
            NetworkOutcome.Rejected => "Failed",
            _ => null, // unknown: leave it Pending
        };
        if (status is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.OutgoingPayments
            .Where(p => p.Id == payment.Id && p.Status == "Pending")
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, status).SetProperty(p => p.ResolvedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 0)
        {
            return null; // someone else resolved it first
        }
        if (status == "Failed")
        {
            await MoveAsync(db, payment.FromAccountId, payment.Amount, "Refund: payment failed", ct);
        }
        await transaction.CommitAsync(ct);
        return status;
    }
    // #endregion resolve

    // A balance change and its transaction row, together: in the caller's transaction if there
    // is one, otherwise in their own.
    // Debits only succeed if the stored balance covers them (stage 2's conditional update).
    private static async Task<bool> MoveAsync(BankDb db, int accountId, decimal change, string description, CancellationToken ct)
    {
        await using var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var updated = await db.Accounts
            .Where(a => a.Id == accountId && (change > 0 || a.Balance >= -change))
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Balance, a => a.Balance + change)
                .SetProperty(a => a.BalanceVersion, a => a.BalanceVersion + 1), ct);
        if (updated == 0)
        {
            return false;
        }
        db.Transactions.Add(new Transaction { AccountId = accountId, Amount = change, Description = description, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        if (own is not null)
        {
            await own.CommitAsync(ct);
        }
        return true;
    }

    private static OutgoingPaymentResponse Response(OutgoingPayment p, string? status = null) => new(p.Id, status ?? p.Status, p.Amount);
}

public enum NetworkOutcome
{
    Settled,
    Rejected,
    NotFound,
    Unknown,
}

/// <summary>The payment network's API (Bank.FakeServices in the lab).</summary>
public sealed class NetworkClient(HttpClient http)
{
    // #region network-call
    // Only a clear answer counts. A timeout, a dropped connection or a 5xx means "unknown":
    // the network may or may not have made the payment.
    public async Task<NetworkOutcome> SendAsync(Guid reference, OutgoingPaymentRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/network/payments",
                new { reference = reference.ToString(), request.Amount, request.ToBank, request.ToAccount }, ct);
            return response.IsSuccessStatusCode ? NetworkOutcome.Settled
                : (int)response.StatusCode is >= 400 and < 500 ? NetworkOutcome.Rejected
                : NetworkOutcome.Unknown;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return NetworkOutcome.Unknown;
        }
    }
    // #endregion network-call

    public async Task<NetworkOutcome> StatusAsync(Guid reference, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"/network/payments/{reference}", ct);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => NetworkOutcome.Settled,
                HttpStatusCode.NotFound => NetworkOutcome.NotFound,
                _ => NetworkOutcome.Unknown,
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return NetworkOutcome.Unknown;
        }
    }
}

/// <summary>
/// B3 stage 6 (Lab:Outgoing=Pending): resolves payments whose outcome is unknown. It asks the
/// network about each one; if the network has no record of it, it sends it again with the same
/// reference, up to three attempts, then fails it and refunds the customer. Lab:Resolve=ResendBlindly
/// sends again without asking.
/// </summary>
public sealed class PendingPaymentResolver(IServiceScopeFactory scopes, IConfiguration config, ILogger<PendingPaymentResolver> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            if (config["Lab:Outgoing"] != "Pending")
            {
                continue;
            }
            try
            {
                await ResolveBatchAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Resolving pending payments failed; trying again shortly");
            }
        }
    }

    // #region resolver
    private async Task ResolveBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        var network = scope.ServiceProvider.GetRequiredService<NetworkClient>();

        // Give the original request time to finish before second-guessing it.
        var settleAfter = DateTimeOffset.UtcNow.AddSeconds(-config.GetValue("Network:ResolveAfterSeconds", 5));
        var pending = await db.OutgoingPayments.AsNoTracking()
            .Where(p => p.Status == "Pending" && p.CreatedAt < settleAfter)
            .OrderBy(p => p.CreatedAt).Take(100).ToListAsync(ct);

        foreach (var payment in pending)
        {
            // Ask first. Only if the network has no record of it is it safe to send again,
            // and then with the same reference, so a network that deduplicates pays it once.
            // (Lab:Resolve=ResendBlindly skips the question, to show why it matters.)
            var outcome = config["Lab:Resolve"] == "ResendBlindly"
                ? NetworkOutcome.NotFound
                : await network.StatusAsync(payment.Id, ct);
            if (outcome == NetworkOutcome.NotFound)
            {
                if (payment.Attempts >= 3)
                {
                    outcome = NetworkOutcome.Rejected; // give up: fail it and refund the customer
                }
                else
                {
                    await db.OutgoingPayments.Where(p => p.Id == payment.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(p => p.Attempts, p => p.Attempts + 1), ct);
                    outcome = await network.SendAsync(payment.Id,
                        new OutgoingPaymentRequest(payment.FromAccountId, payment.Amount, payment.ToBank, payment.ToAccount), ct);
                }
            }
            await OutgoingPayments.ResolveAsync(db, payment, outcome, ct);
        }
    }
    // #endregion resolver
}
