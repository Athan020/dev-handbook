using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

/// <summary>
/// B4 stage 5 (Fraud:Fallback=AllowSmall): transfers held because the fraud service couldn't
/// decide are asked about again every couple of seconds. Approved ones are made; declined ones
/// are refused; the rest wait. Held transfers aren't lost when the fraud service is down.
/// </summary>
public sealed class HeldTransferReleaser(IServiceScopeFactory scopes, IConfiguration config, ILogger<HeldTransferReleaser> log) : BackgroundService
{
    public static IEndpointRouteBuilder MapFraudReviewEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/fraud-reviews/summary", async (BankDb db, CancellationToken ct) =>
            await db.FraudReviews.GroupBy(r => r.Status)
                .Select(g => new { status = g.Key, count = g.Count(), amount = g.Sum(r => r.Amount) })
                .ToListAsync(ct));
        return app;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            if (config["Fraud:Fallback"] != "AllowSmall")
            {
                continue;
            }
            try
            {
                await ReleaseBatchAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Releasing held transfers failed; trying again shortly");
            }
        }
    }

    // #region release
    private async Task ReleaseBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        var fraud = scope.ServiceProvider.GetRequiredService<FraudClient>();

        var held = await db.FraudReviews.Where(r => r.Status == "Held").OrderBy(r => r.CreatedAt).Take(200).ToListAsync(ct);
        foreach (var review in held)
        {
            var outcome = await fraud.DecideAsync(review.FromAccountId, review.ToAccountId, review.Amount, ct);
            if (outcome.Decision == FraudDecision.Unavailable)
            {
                return; // still no decisions to be had: try the whole batch again later
            }

            var transfer = outcome.Decision == FraudDecision.Approved
                ? await TransferEndpoints.MakeHeldTransferAsync(db, new TransferRequest(review.FromAccountId, review.ToAccountId, review.Amount, review.Reference), config, ct)
                : null;
            review.Status = transfer is null ? "Declined" : "Released";
            review.TransferId = transfer?.TransferId;
            review.ResolvedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
    // #endregion release
}
