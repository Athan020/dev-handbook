using Bank.Modular.Contracts.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bank.Modular.Statements;

/// <summary>A statement line, copied from the ledger's feed. Statements own this copy.</summary>
internal sealed class LineRow
{
    public long Position { get; set; }
    public Guid TransferId { get; set; }
    public int AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; }
}

/// <summary>How far through the ledger's feed the copy has got.</summary>
internal sealed class FeedPosition
{
    public string Feed { get; set; } = "";
    public long Position { get; set; }
    public DateTimeOffset? LastPostedAt { get; set; }

    /// <summary>When the copy last read the feed to the end: it was complete as of then.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}

internal sealed class StatementsDb(DbContextOptions<StatementsDb> options) : DbContext(options)
{
    public DbSet<LineRow> Lines => Set<LineRow>();
    public DbSet<FeedPosition> FeedPositions => Set<FeedPosition>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(StatementsModule.Schema);
        model.Entity<LineRow>(line =>
        {
            line.ToTable("lines");
            // The ledger's position is the key, so a line can't be copied twice.
            line.HasKey(l => l.Position);
            line.Property(l => l.Position).ValueGeneratedNever();
            line.Property(l => l.Amount).HasPrecision(18, 2);
            line.Property(l => l.Reference).HasMaxLength(200);
            line.HasIndex(l => new { l.AccountId, l.PostedAt });
        });
        model.Entity<FeedPosition>(position =>
        {
            position.ToTable("feed_positions");
            position.HasKey(p => p.Feed);
            position.Property(p => p.Feed).HasMaxLength(50);
        });
    }
}

internal sealed record CopyOptions(TimeSpan Interval, int PageSize);

// #region follow-feed
/// <summary>
/// Keeps the statements' copy of the ledger's entries up to date: every interval, reads the
/// ledger's feed from where it got to last time, and saves the new lines and the new position
/// in one transaction. A crash between pages loses nothing and copies nothing twice.
/// </summary>
internal sealed class LedgerFeedFollower(IServiceScopeFactory scopes, CopyOptions options, TimeProvider time, ILogger<LedgerFeedFollower> logger)
    : BackgroundService
{
    public const string Feed = "ledger";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await CopyNextPageAsync(stoppingToken) == options.PageSize)
                {
                    // A full page: there may be more waiting, so carry on without pausing.
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The ledger is unavailable, or slow. The copy stays as it is, and statements
                // keep being served from it; try again next time round.
                logger.LogWarning("Couldn't read the ledger's feed: {Error}", e.Message);
            }
            await Task.Delay(options.Interval, time, stoppingToken);
        }
    }

    private async Task<int> CopyNextPageAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatementsDb>();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();

        var position = await db.FeedPositions.SingleAsync(p => p.Feed == Feed, ct);
        var entries = await ledger.ReadFeedAsync(position.Position, options.PageSize, ct);
        position.CheckedAt = time.GetUtcNow();
        if (entries.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return 0;
        }

        db.Lines.AddRange(entries.Select(e => new LineRow
        {
            Position = e.Position, TransferId = e.TransferId, AccountId = e.AccountId,
            Amount = e.Amount, Reference = e.Reference, PostedAt = e.PostedAt,
        }));
        position.Position = entries[^1].Position;
        position.LastPostedAt = entries[^1].PostedAt;
        await db.SaveChangesAsync(ct); // the lines and the position, together
        return entries.Count;
    }
}
// #endregion follow-feed
