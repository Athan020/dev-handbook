using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;

namespace Bank.Api.Endpoints;

/// <summary>Balance and activity for an account, computed from its whole history.</summary>
public sealed record AccountSummary(
    int AccountId, decimal Balance, int TransactionCount, DateTimeOffset? LastTransactionAt, DateTimeOffset ComputedAt);

public sealed record NewTransaction(decimal Amount, string Description);

/// <summary>
/// B1 stage 5. The summary is an expensive, frequently requested read, so it's the
/// candidate for caching. The Lab:Summary setting picks the version, so you can compare:
/// None (the default: query every time), CacheAside (by hand, with IMemoryCache) and
/// Hybrid (HybridCache, which only lets one caller per key recompute at a time).
/// Lab:CacheSeconds sets how long entries live (default 30).
/// </summary>
public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:int}/summary", GetSummary);
        app.MapPost("/accounts/{id:int}/transactions", AddTransaction);
        return app;
    }

    private static string CacheKey(int accountId) => $"account-summary:{accountId}";

    private static async Task<IResult> GetSummary(
        int id, HttpContext http, ReadRouting routing, IConfiguration config, IMemoryCache memory, HybridCache hybrid,
        CancellationToken ct)
    {
        // Stage 6: which database serves this read (the primary unless Lab:Reads says otherwise).
        var db = await routing.ChooseAsync(http, ct);
        var ttl = TimeSpan.FromSeconds(config.GetValue("Lab:CacheSeconds", 30));
        var summary = config["Lab:Summary"] switch
        {
            "CacheAside" => await CacheAsideAsync(id, db, memory, ttl, ct),
            "Hybrid" => await HybridAsync(id, db, hybrid, ttl, ct),
            _ => await ComputeSummaryAsync(db, id, ct),
        };

        return summary is null ? Results.NotFound() : Results.Ok(summary);
    }

    // #region compute
    // One query: the database sums the account's whole history. For the busiest
    // account that means reading over 200,000 rows on every request.
    private static async Task<AccountSummary?> ComputeSummaryAsync(BankDb db, int id, CancellationToken ct)
    {
        var totals = await db.Transactions
            .Where(t => t.AccountId == id)
            .GroupBy(t => t.AccountId)
            .Select(g => new { Balance = g.Sum(t => t.Amount), Count = g.Count(), Last = g.Max(t => t.CreatedAt) })
            .SingleOrDefaultAsync(ct);

        if (totals is null)
        {
            return await db.Accounts.AnyAsync(a => a.Id == id, ct)
                ? new AccountSummary(id, 0m, 0, null, DateTimeOffset.UtcNow)
                : null;
        }

        return new AccountSummary(id, totals.Balance, totals.Count, totals.Last, DateTimeOffset.UtcNow);
    }
    // #endregion compute

    // #region cache-aside
    // Cache-aside by hand: look in the cache; on a miss, compute and store the result.
    // Simple, but when a popular entry expires, every request that misses at that moment
    // computes it at once: a cache stampede.
    private static async Task<AccountSummary?> CacheAsideAsync(
        int id, BankDb db, IMemoryCache cache, TimeSpan ttl, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(id), out AccountSummary? cached))
        {
            return cached;
        }

        var summary = await ComputeSummaryAsync(db, id, ct);
        if (summary is not null)
        {
            cache.Set(CacheKey(id), summary, ttl);
        }

        return summary;
    }
    // #endregion cache-aside

    // #region hybrid
    // HybridCache does the same, but only one caller per key runs the factory at a time;
    // the others wait for its result. It can also use a shared second-level cache, such as
    // Redis, when one is configured.
    private static async Task<AccountSummary?> HybridAsync(
        int id, BankDb db, HybridCache cache, TimeSpan ttl, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            CacheKey(id),
            (db, id),
            static async (state, token) => await ComputeSummaryAsync(state.db, state.id, token),
            new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl },
            cancellationToken: ct);
    }
    // #endregion hybrid

    /// <summary>
    /// Adds a transaction to one account, so the labs can change data and watch caches
    /// and replicas catch up. This is not how to move money safely; journey B3 covers that.
    /// </summary>
    private static async Task<IResult> AddTransaction(
        int id, NewTransaction input, HttpContext http, BankDb db, IMemoryCache memory, HybridCache hybrid,
        CancellationToken ct)
    {
        if (input.Amount == 0 || string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction"] = ["amount must not be zero, and description must be 1 to 200 characters."],
            });
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return Results.NotFound();
        }

        var transaction = new Transaction
        {
            AccountId = id,
            Amount = input.Amount,
            Description = input.Description,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // B3 stage 2: keep the stored balance in step with the history, in the same transaction.
        await using (var dbTransaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Balance, a => a.Balance + input.Amount)
                .SetProperty(a => a.BalanceVersion, a => a.BalanceVersion + 1), ct);
            await dbTransaction.CommitAsync(ct);
        }

        // #region invalidate
        // Invalidate after the write has committed, so a reader can't re-cache the old value
        // from before the write. Each API instance has its own memory cache, so this only
        // clears this instance's copy; the TTL bounds how stale the others can be.
        memory.Remove(CacheKey(id));
        await hybrid.RemoveAsync(CacheKey(id), ct);
        // #endregion invalidate

        // Stage 6: hand the client the primary's WAL position after this commit, so its next
        // read can wait for a replica that has caught up (see ReadRouting).
        http.Response.Headers[ReadRouting.TokenHeader] = await ReadRouting.CurrentLsnAsync(db, ct);

        return Results.Created(
            $"/accounts/{id}/transactions/{transaction.Id}",
            new TransactionResponse(transaction.Id, transaction.Amount, transaction.Description, transaction.CreatedAt, null));
    }
}
