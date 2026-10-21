using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bank.Modular.Ledger;

// B5 stage 5: topping up a prepaid card. The money leaves the customer's account in the ledger
// and arrives on the card in the cards service, which has its own database. No transaction
// covers both, so the top-up is a saga: a sequence of local steps, each undone by a
// compensating step if a later one can't happen.
//
// Lab:TopUps chooses how it's run:
//   Naive    - debit, then credit the card; if that fails, nothing undoes the debit.
//   InMemory - the same, but a failure triggers a refund, from code that only exists in memory.
//   Saga     - every step's outcome is saved with the step itself, and an orchestrator
//              carries each top-up forward until it's completed or refunded.

internal static class TopUpStatus
{
    public const string Debited = "Debited";     // money taken from the customer, card not yet credited
    public const string Completed = "Completed"; // the card was credited
    public const string Refunded = "Refunded";   // the money went back to the customer
}

internal sealed class CardTopUp
{
    public Guid TopUpId { get; set; }
    public int AccountId { get; set; }
    public string CardId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = TopUpStatus.Debited;
    public string? Detail { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

internal enum TopUpMode { Naive, InMemory, Saga }

internal sealed record TopUpOptions(TopUpMode Mode, double CrashRate, TimeSpan Lease, int BatchSize);

internal sealed record TopUpRequest(int AccountId, string CardId, decimal Amount);

/// <summary>Thrown to simulate the process dying at that point: nothing in memory survives it.</summary>
internal sealed class SimulatedCrashException() : Exception("Simulated crash.");

internal enum CardCredit { Credited, Declined }

internal sealed class CardsClient(HttpClient http)
{
    /// <summary>
    /// Credits the card. The top-up's id is the reference, and the cards service applies each
    /// reference once, so asking again after a timeout is safe. Throws if there's no answer.
    /// </summary>
    public async Task<CardCredit> CreditAsync(string cardId, Guid reference, decimal amount, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"/cards/{Uri.EscapeDataString(cardId)}/top-ups", new { reference = reference.ToString(), amount }, ct);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return CardCredit.Declined;
        response.EnsureSuccessStatusCode();
        return CardCredit.Credited;
    }
}

// #region steps
/// <summary>The local steps of a top-up, each saved together with the money it moves.</summary>
internal sealed class TopUpSteps(LedgerDb db, Transfers transfers, TimeProvider time)
{
    /// <summary>Step 1: take the money from the customer, and record the top-up, in one transaction.</summary>
    public async Task<TransferOutcome> DebitAsync(Guid topUpId, TopUpRequest request, CancellationToken ct)
    {
        var (outcome, _) = await transfers.MoveAsync(
            request.AccountId, LedgerSeed.CardTopUpsInTransit, request.Amount, $"Card top-up {request.CardId}", ct,
            alongside: _ =>
            {
                var now = time.GetUtcNow();
                db.CardTopUps.Add(new CardTopUp
                {
                    TopUpId = topUpId, AccountId = request.AccountId, CardId = request.CardId, Amount = request.Amount,
                    CreatedAt = now, NextAttemptAt = now,
                });
                return Task.FromResult(true);
            });
        return outcome;
    }

    /// <summary>The card was credited: pass the money on, and mark the top-up completed.</summary>
    public Task CompleteAsync(CardTopUp topUp, CancellationToken ct) =>
        FinishAsync(topUp, LedgerSeed.CardFunding, TopUpStatus.Completed, null, ct);

    /// <summary>The compensating step: give the money back, and mark the top-up refunded.</summary>
    public Task RefundAsync(CardTopUp topUp, string reason, CancellationToken ct) =>
        FinishAsync(topUp, topUp.AccountId, TopUpStatus.Refunded, reason, ct);

    // Moves the money out of transit only if the top-up is still in progress, so a step that
    // runs twice, after a retry or on two processors, moves nothing the second time.
    private Task FinishAsync(CardTopUp topUp, int toAccountId, string status, string? detail, CancellationToken ct) =>
        transfers.MoveAsync(LedgerSeed.CardTopUpsInTransit, toAccountId, topUp.Amount, $"Card top-up {topUp.CardId}: {status.ToLowerInvariant()}", ct,
            alongside: async ct => await db.CardTopUps
                .Where(t => t.TopUpId == topUp.TopUpId && t.Status == TopUpStatus.Debited)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.Detail, detail)
                    .SetProperty(t => t.FinishedAt, time.GetUtcNow()), ct) == 1);
}
// #endregion steps

internal static class CardTopUpEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/card-top-ups", async (TopUpRequest request, TopUpOptions options, TopUpSteps steps,
            CardsClient cards, LedgerDb db, CancellationToken ct) =>
        {
            if (request.Amount <= 0) return Results.Problem(statusCode: 400, title: "The amount must be positive.");

            var topUpId = Guid.CreateVersion7();
            var debited = await steps.DebitAsync(topUpId, request, ct);
            if (debited == TransferOutcome.InsufficientFunds) return Results.Problem(statusCode: 422, title: "Insufficient funds.");
            if (debited != TransferOutcome.Completed) return Results.Problem(statusCode: 404, title: "No such account.");

            // The saga's orchestrator takes it from here; the customer follows its status.
            if (options.Mode == TopUpMode.Saga)
                return Results.Accepted($"/card-top-ups/{topUpId}", new { topUpId, Status = TopUpStatus.Debited });

            if (Random.Shared.NextDouble() < options.CrashRate) throw new SimulatedCrashException();
            var topUp = await db.CardTopUps.AsNoTracking().SingleAsync(t => t.TopUpId == topUpId, ct);

            // #region in-memory-compensation
            try
            {
                if (await cards.CreditAsync(request.CardId, topUpId, request.Amount, ct) == CardCredit.Declined)
                {
                    if (options.Mode == TopUpMode.InMemory) await steps.RefundAsync(topUp, "The card was declined.", ct);
                    return Results.Problem(statusCode: 422, title: "The card was declined.");
                }
            }
            catch (Exception e) when (options.Mode == TopUpMode.InMemory && e is HttpRequestException or TaskCanceledException)
            {
                // No answer from the cards service, so undo the debit. But "no answer" isn't
                // "not credited": if the reply was lost, the card has the money too.
                await steps.RefundAsync(topUp, "The cards service didn't answer.", ct);
                return Results.Problem(statusCode: 503, title: "The top-up failed, and your money has been returned.");
            }
            // #endregion in-memory-compensation

            await steps.CompleteAsync(topUp, ct);
            return Results.Created($"/card-top-ups/{topUpId}", new { topUpId, Status = TopUpStatus.Completed });
        });

        app.MapGet("/card-top-ups/{topUpId:guid}", async (Guid topUpId, LedgerDb db, CancellationToken ct) =>
            await db.CardTopUps.AsNoTracking().FirstOrDefaultAsync(t => t.TopUpId == topUpId, ct) is { } topUp
                ? Results.Ok(topUp)
                : Results.NotFound());

        // One account's top-ups. The account is in the path, so a gateway can check it's yours.
        app.MapGet("/accounts/{accountId:int}/card-top-ups", async (int accountId, LedgerDb db, CancellationToken ct) =>
            Results.Ok(await db.CardTopUps.AsNoTracking()
                .Where(t => t.AccountId == accountId)
                .OrderBy(t => t.CreatedAt)
                .Select(t => new { t.TopUpId, t.CardId, t.Amount, t.Status, t.CreatedAt })
                .ToListAsync(ct)));

        // Every top-up, for reconciling against the cards service's own record. Not for customers.
        app.MapGet("/card-top-ups", async (LedgerDb db, CancellationToken ct) =>
            Results.Ok(await db.CardTopUps.AsNoTracking()
                .OrderBy(t => t.CreatedAt)
                .Select(t => new { t.TopUpId, t.AccountId, t.CardId, t.Amount, t.Status, t.Attempts, t.Detail })
                .ToListAsync(ct)));
    }
}

// #region saga-orchestrator
/// <summary>
/// Carries each debited top-up forward. It credits the card, retrying through timeouts and
/// outages (safe, because the cards service applies each reference once), and then completes
/// the top-up; only a definite refusal from the cards service makes it refund. Its state is
/// the top-up's row, so a crash, a restart or a second processor picks up where it left off.
/// </summary>
internal sealed class CardTopUpOrchestrator(IServiceScopeFactory scopes, TopUpOptions options, TimeProvider time, ILogger<CardTopUpOrchestrator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                var topUps = await ClaimAsync(stoppingToken);
                claimed = topUps.Count;
                await Task.WhenAll(topUps.Select(topUp => AdvanceAsync(topUp, stoppingToken)));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("Advancing card top-ups failed: {Error}", e.Message);
            }
            if (claimed < options.BatchSize) await Task.Delay(TimeSpan.FromMilliseconds(100), time, stoppingToken);
        }
    }

    private async Task<List<CardTopUp>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
        var now = time.GetUtcNow();
        return await db.CardTopUps.FromSql($"""
            UPDATE ledger.card_top_ups
            SET next_attempt_at = {now + options.Lease}, attempts = attempts + 1
            WHERE top_up_id IN (
                SELECT top_up_id FROM ledger.card_top_ups
                WHERE status = {TopUpStatus.Debited} AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);
    }

    private async Task AdvanceAsync(CardTopUp topUp, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var steps = scope.ServiceProvider.GetRequiredService<TopUpSteps>();
        if (Random.Shared.NextDouble() < options.CrashRate)
        {
            // As if the process died here: the claim lapses when its lease runs out, and the
            // top-up is picked up again, from its saved state.
            logger.LogInformation("Simulated crash while advancing top-up {TopUpId}", topUp.TopUpId);
            return;
        }
        try
        {
            var credit = await scope.ServiceProvider.GetRequiredService<CardsClient>().CreditAsync(topUp.CardId, topUp.TopUpId, topUp.Amount, ct);
            if (credit == CardCredit.Credited) await steps.CompleteAsync(topUp, ct);
            else await steps.RefundAsync(topUp, "The card was declined.", ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            // No answer: the card may or may not have been credited. Ask again later, with the
            // same reference, until the cards service gives a definite answer.
            var backoff = TimeSpan.FromMilliseconds(Math.Min(200 * Math.Pow(2, topUp.Attempts - 1), 5000));
            var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
            await db.CardTopUps.Where(t => t.TopUpId == topUp.TopUpId && t.Status == TopUpStatus.Debited)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.NextAttemptAt, time.GetUtcNow() + backoff), ct);
        }
    }
}
// #endregion saga-orchestrator
