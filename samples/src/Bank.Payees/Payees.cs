using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Payees;

public sealed record Payee(Guid PayeeId, int CustomerAccountId, int PayeeAccountId, string Name, string Reference, DateTimeOffset AddedAt);

public sealed record AddPayeeRequest(int PayeeAccountId, string Name, string? Reference);

public sealed record PayeeChange(long Position, string Kind, Guid PayeeId, int CustomerAccountId, int PayeeAccountId,
    string Name, string Reference, DateTimeOffset AddedAt, DateTimeOffset ChangedAt);

/// <summary>How a customer's payees are listed: "AddedAt" (newest last), or "Name".</summary>
public sealed record ListOrder(string By);

public sealed class PayeeRow
{
    public Guid PayeeId { get; set; }
    public int CustomerAccountId { get; set; }
    public int PayeeAccountId { get; set; }
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }

    public Payee ToContract() => new(PayeeId, CustomerAccountId, PayeeAccountId, Name, Reference, AddedAt);
}

public sealed class PayeeChangeRow
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

public sealed class FollowPositionRow
{
    public string Source { get; set; } = "";
    public long Position { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class PayeesServiceDb(DbContextOptions<PayeesServiceDb> options) : DbContext(options)
{
    public DbSet<PayeeRow> Payees => Set<PayeeRow>();
    public DbSet<PayeeChangeRow> PayeeChanges => Set<PayeeChangeRow>();
    public DbSet<FollowPositionRow> FollowPositions => Set<FollowPositionRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PayeeRow>(payee =>
        {
            payee.ToTable("payees");
            payee.HasKey(p => p.PayeeId);
            payee.Property(p => p.Name).HasMaxLength(200);
            payee.Property(p => p.Reference).HasMaxLength(200);
            payee.HasIndex(p => new { p.CustomerAccountId, p.PayeeAccountId }).IsUnique();
        });
        model.Entity<PayeeChangeRow>(change =>
        {
            change.ToTable("payee_changes");
            change.HasKey(c => c.Position);
            change.Property(c => c.Kind).HasMaxLength(20);
            change.Property(c => c.Name).HasMaxLength(200);
            change.Property(c => c.Reference).HasMaxLength(200);
        });
        model.Entity<FollowPositionRow>(position =>
        {
            position.ToTable("follow_positions");
            position.HasKey(p => p.Source);
            position.Property(p => p.Source).HasMaxLength(200);
        });
    }
}

public sealed class PayeeStore(PayeesServiceDb db, ListOrder order, TimeProvider time)
{
    public Task<Payee?> GetAsync(int customerAccountId, Guid payeeId, CancellationToken ct) =>
        db.Payees.AsNoTracking()
            .Where(p => p.CustomerAccountId == customerAccountId && p.PayeeId == payeeId)
            .Select(p => p.ToContract())
            .FirstOrDefaultAsync(ct);

    // #region list-order
    public async Task<IReadOnlyList<Payee>> ListAsync(int customerAccountId, CancellationToken ct)
    {
        var payees = db.Payees.AsNoTracking().Where(p => p.CustomerAccountId == customerAccountId);
        // The rewrite lists payees in the order they were added. The module it replaces
        // listed them by name, and the app has relied on that.
        var ordered = order.By == "Name"
            ? payees.OrderBy(p => p.Name).ThenBy(p => p.PayeeId)
            : payees.OrderBy(p => p.AddedAt).ThenBy(p => p.PayeeId);
        return await ordered.Select(p => p.ToContract()).ToListAsync(ct);
    }
    // #endregion list-order

    public async Task<Payee?> AddAsync(int customerAccountId, AddPayeeRequest request, CancellationToken ct)
    {
        var row = new PayeeRow
        {
            PayeeId = Guid.CreateVersion7(), CustomerAccountId = customerAccountId, PayeeAccountId = request.PayeeAccountId,
            Name = request.Name.Trim(), Reference = request.Reference?.Trim() ?? "", AddedAt = time.GetUtcNow(),
        };
        db.Payees.Add(row);
        db.PayeeChanges.Add(PayeeChangeRow.For("Added", row, row.AddedAt));
        try
        {
            await db.SaveChangesAsync(ct);
            return row.ToContract();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
    }

    public async Task<bool> RemoveAsync(int customerAccountId, Guid payeeId, CancellationToken ct)
    {
        var row = await db.Payees.FirstOrDefaultAsync(p => p.CustomerAccountId == customerAccountId && p.PayeeId == payeeId, ct);
        if (row is null) return false;
        db.Payees.Remove(row);
        db.PayeeChanges.Add(PayeeChangeRow.For("Removed", row, time.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}

public static class PayeeFeed
{
    // A change whose transaction fails, such as a payee added twice, leaves a gap that never
    // fills, and the feed waits this long before passing it. Short, because payee transactions
    // are short; too short, and a slow commit could be skipped.
    private static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Changes after a position, stopping at a gap a change still being committed may fill.</summary>
    public static async Task<List<PayeeChange>> ReadAsync(PayeesServiceDb db, long after, int max, DateTimeOffset now, CancellationToken ct)
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

public sealed record FollowOptions(Uri Source, TimeSpan Interval);

/// <summary>Applies another store's changes to this one, without logging them again.</summary>
public sealed class PayeeFollower(IServiceScopeFactory scopes, IHttpClientFactory clients, FollowOptions options, TimeProvider time, ILogger<PayeeFollower> logger)
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
        var db = scope.ServiceProvider.GetRequiredService<PayeesServiceDb>();
        var source = options.Source.ToString();
        var position = await db.FollowPositions.FindAsync([source], ct);
        if (position is null) db.FollowPositions.Add(position = new FollowPositionRow { Source = source });

        var changes = await clients.CreateClient(HttpClientName)
            .GetFromJsonAsync<List<PayeeChange>>(new Uri(options.Source, $"/payee-changes?after={position.Position}&max=500"), ct) ?? [];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var change in changes)
        {
            if (change.Kind == "Added")
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO payees (payee_id, customer_account_id, payee_account_id, name, reference, added_at)
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
