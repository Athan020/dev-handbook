using Bank.Modular.Contracts.Payees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bank.Modular.Ledger;

internal static class TransferStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
}

/// <summary>
/// B5 stage 4 (Lab:Transfers=Accept): a transfer the ledger has accepted but not yet made. Its
/// id becomes the transfer's id once the money moves.
/// </summary>
internal sealed class TransferRequest
{
    public Guid TransferId { get; set; }
    public int FromAccountId { get; set; }
    public Guid PayeeId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = TransferStatus.Pending;
    public string? Reason { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

internal sealed record TransferMode(bool Accept);

internal sealed record ProcessorOptions(int BatchSize, TimeSpan Lease, TimeSpan MaxBackoff);

// #region process-requests
/// <summary>
/// Makes accepted transfers: resolves the payee, runs the checks, moves the money. A request
/// whose dependencies can't answer is tried again later, with backoff; one that can't succeed
/// (no such payee, not enough money) is rejected. Nothing waits on the customer's request.
/// </summary>
internal sealed class TransferProcessor(IServiceScopeFactory scopes, ProcessorOptions options, TimeProvider time, ILogger<TransferProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                var requests = await ClaimAsync(stoppingToken);
                claimed = requests.Count;
                await Task.WhenAll(requests.Select(request => ProcessAsync(request, stoppingToken)));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // An unhandled exception would stop the whole host. Leased requests become due
                // again when their lease runs out, so nothing is lost by carrying on.
                logger.LogWarning("Processing transfers failed: {Error}", e.Message);
            }
            if (claimed < options.BatchSize) await Task.Delay(TimeSpan.FromMilliseconds(100), time, stoppingToken);
        }
    }

    /// <summary>
    /// Takes due requests, and leases them: until the lease runs out, no other processor takes
    /// them. If this one crashes, they become due again when it does.
    /// </summary>
    private async Task<List<TransferRequest>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
        var now = time.GetUtcNow();
        return await db.TransferRequests.FromSql($"""
            UPDATE ledger.transfer_requests
            SET next_attempt_at = {now + options.Lease}, attempts = attempts + 1
            WHERE transfer_id IN (
                SELECT transfer_id FROM ledger.transfer_requests
                WHERE status = {TransferStatus.Pending} AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);
    }

    private async Task ProcessAsync(TransferRequest request, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        try
        {
            // No database transaction is open while the other services are asked (B4 stage 1).
            var payee = await services.GetRequiredService<IPayees>().GetAsync(request.FromAccountId, request.PayeeId, ct);
            if (payee is null)
            {
                await RejectAsync(services, request, "No such payee for this account.", ct);
                return;
            }
            await services.GetRequiredService<TransferChecks>().RunAsync(request.FromAccountId, payee.PayeeAccountId, request.Amount, ct);

            // Moving the money also marks the request completed, in one transaction, so a
            // request can't be paid twice even if two processors reach this point.
            var (outcome, _) = await services.GetRequiredService<Transfers>().MoveAsync(
                request.FromAccountId, payee.PayeeAccountId, request.Amount, request.Reference ?? payee.Reference, ct, completes: request.TransferId);
            if (outcome == TransferOutcome.InsufficientFunds) await RejectAsync(services, request, "Insufficient funds.", ct);
            else if (outcome == TransferOutcome.UnknownAccount) await RejectAsync(services, request, "No such account.", ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            // A dependency is down or slow: try again later, backing off with each attempt.
            var backoff = TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, request.Attempts - 1), options.MaxBackoff.TotalMilliseconds));
            var jittered = backoff * (0.5 + Random.Shared.NextDouble() / 2);
            var db = services.GetRequiredService<LedgerDb>();
            await db.TransferRequests.Where(r => r.TransferId == request.TransferId && r.Status == TransferStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.NextAttemptAt, time.GetUtcNow() + jittered), ct);
            logger.LogDebug("Transfer {TransferId} will be retried: {Error}", request.TransferId, e.Message);
        }
    }

    private async Task RejectAsync(IServiceProvider services, TransferRequest request, string reason, CancellationToken ct)
    {
        var db = services.GetRequiredService<LedgerDb>();
        await db.TransferRequests.Where(r => r.TransferId == request.TransferId && r.Status == TransferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, TransferStatus.Rejected)
                .SetProperty(r => r.Reason, reason)
                .SetProperty(r => r.FinishedAt, time.GetUtcNow()), ct);
    }
}
// #endregion process-requests
