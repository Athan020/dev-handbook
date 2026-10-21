using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bank.Modular.Payees;

// B5 stage 7: moving payees out of the monolith. Every change the module makes is also written
// to a change log, in the same transaction, and published as a feed. A new payees service
// follows the feed to build and keep its copy; once it owns the writes, this module follows
// the new service's feed in turn, so switching back stays possible.

internal static class ChangeKind
{
    public const string Added = "Added";
    public const string Removed = "Removed";
}

/// <summary>One change to a payee, in the order the module made them.</summary>
internal sealed class PayeeChangeRow
{
    public long Position { get; set; }
    public string Kind { get; set; } = "";
    public Guid PayeeId { get; set; }
    public int CustomerAccountId { get; set; }
    public int PayeeAccountId { get; set; }
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }

    public static PayeeChangeRow For(string kind, PayeeRow payee, DateTimeOffset at) => new()
    {
        Kind = kind, PayeeId = payee.PayeeId, CustomerAccountId = payee.CustomerAccountId, PayeeAccountId = payee.PayeeAccountId,
        Name = payee.Name, Reference = payee.Reference, AddedAt = payee.AddedAt, ChangedAt = at,
    };
}

/// <summary>How far this module has got through another store's feed.</summary>
internal sealed class FollowPositionRow
{
    public string Source { get; set; } = "";
    public long Position { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>A change as it travels between stores.</summary>
internal sealed record PayeeChange(long Position, string Kind, Guid PayeeId, int CustomerAccountId, int PayeeAccountId,
    string Name, string Reference, DateTimeOffset AddedAt, DateTimeOffset ChangedAt);

internal static class PayeeFeed
{
    // A change whose transaction fails, such as a payee added twice, leaves a gap that never
    // fills, and the feed waits this long before passing it. Short, because payee transactions
    // are short; too short, and a slow commit could be skipped.
    private static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Changes after a position, stopping at a gap that may still be filled by a change being
    /// committed (as the ledger's feed does, stage 3).
    /// </summary>
    public static async Task<List<PayeeChange>> ReadAsync(PayeesDb db, long after, int max, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.PayeeChanges.AsNoTracking()
            .Where(c => c.Position > after).OrderBy(c => c.Position).Take(max)
            .Select(c => new PayeeChange(c.Position, c.Kind, c.PayeeId, c.CustomerAccountId, c.PayeeAccountId, c.Name, c.Reference, c.AddedAt, c.ChangedAt))
            .ToListAsync(ct);
        var feed = new List<PayeeChange>(rows.Count);
        var expected = after + 1;
        foreach (var row in rows)
        {
            if (row.Position != expected && now - row.ChangedAt < GapTimeout) break;
            feed.Add(row);
            expected = row.Position + 1;
        }
        return feed;
    }
}

internal sealed record FollowOptions(Uri Source, TimeSpan Interval);

// #region follow-changes
/// <summary>
/// Applies another store's changes to this one: payees added there are added here, payees
/// removed there are removed here. Applied changes aren't logged again, so two stores can
/// follow each other without echoing. The changes and the position are saved together.
/// </summary>
internal sealed class PayeeFollower(IServiceScopeFactory scopes, IHttpClientFactory clients, FollowOptions options, TimeProvider time, ILogger<PayeeFollower> logger)
    : BackgroundService
{
    public const string HttpClientName = "payee-follower";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ApplyNextPageAsync(stoppingToken) == 500) { }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogDebug("Couldn't follow {Source}: {Error}", options.Source, e.Message);
            }
            await Task.Delay(options.Interval, time, stoppingToken);
        }
    }

    private async Task<int> ApplyNextPageAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayeesDb>();
        var source = options.Source.ToString();
        var position = await db.FollowPositions.FindAsync([source], ct);
        if (position is null) db.FollowPositions.Add(position = new FollowPositionRow { Source = source });

        var changes = await clients.CreateClient(HttpClientName)
            .GetFromJsonAsync<List<PayeeChange>>(new Uri(options.Source, $"/payee-changes?after={position.Position}&max=500"), ct) ?? [];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var change in changes)
        {
            if (change.Kind == ChangeKind.Added)
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO payees.payees (payee_id, customer_account_id, payee_account_id, name, reference, added_at)
                    VALUES ({change.PayeeId}, {change.CustomerAccountId}, {change.PayeeAccountId}, {change.Name}, {change.Reference}, {change.AddedAt})
                    ON CONFLICT DO NOTHING
                    """, ct);
            else
                await db.Payees.Where(p => p.PayeeId == change.PayeeId).ExecuteDeleteAsync(ct);
        }
        if (changes.Count > 0) position.Position = changes[^1].Position;
        position.CheckedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return changes.Count;
    }
}
// #endregion follow-changes
