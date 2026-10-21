using Bank.Modular.Contracts.Payees;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Bank.Modular.Payees;

/// <summary>The payees module: the people each customer pays. Its only public type.</summary>
public static class PayeesModule
{
    public const string Schema = "payees";

    public static IServiceCollection AddPayeesModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PayeesDb>(options => options
            .UseNpgsql(config.GetConnectionString("Payees") ?? config.GetConnectionString("Modular"),
                // The module records its own migrations, in its own schema.
                npgsql => npgsql.MigrationsHistoryTable("__migrations", Schema))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<PayeeStore>();
        services.AddScoped<IPayees>(sp => sp.GetRequiredService<PayeeStore>());
        services.AddTransient(_ => new PayeesSeed(config.GetValue("Modular:SeedAccounts", 1000)));

        // B5 stage 7: follow another payees store's changes, such as the new payees service's.
        if (config["Payees:FollowUrl"] is { } follow)
        {
            services.AddSingleton(new FollowOptions(new Uri(follow), TimeSpan.FromMilliseconds(config.GetValue("Payees:FollowIntervalMs", 200))));
            services.AddHttpClient(PayeeFollower.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(5));
            services.AddHostedService<PayeeFollower>();
        }
        return services;
    }

    public static IEndpointRouteBuilder MapPayees(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{accountId:int}/payees", async (int accountId, PayeeStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(accountId, ct)));

        app.MapGet("/accounts/{accountId:int}/payees/{payeeId:guid}", async (int accountId, Guid payeeId, PayeeStore store, CancellationToken ct) =>
            await store.GetAsync(accountId, payeeId, ct) is { } payee ? Results.Ok(payee) : Results.NotFound());

        app.MapPost("/accounts/{accountId:int}/payees", async (int accountId, AddPayeeRequest request, PayeeStore store, CancellationToken ct) =>
        {
            if (request.PayeeAccountId == accountId) return Results.Problem(statusCode: 400, title: "You can't add yourself as a payee.");
            var payee = await store.AddAsync(accountId, request, ct);
            return payee is null
                ? Results.Problem(statusCode: 409, title: "That account is already one of your payees.")
                : Results.Created($"/accounts/{accountId}/payees/{payee.PayeeId}", payee);
        });

        app.MapDelete("/accounts/{accountId:int}/payees/{payeeId:guid}", async (int accountId, Guid payeeId, PayeeStore store, CancellationToken ct) =>
            await store.RemoveAsync(accountId, payeeId, ct) ? Results.NoContent() : Results.NotFound());

        // Stage 7: the change feed, where it has got to, and how far this store has followed another.
        app.MapGet("/payee-changes", async (long after, int? max, PayeesDb db, TimeProvider time, CancellationToken ct) =>
            Results.Ok(await PayeeFeed.ReadAsync(db, after, Math.Clamp(max ?? 500, 1, 5000), time.GetUtcNow(), ct)));
        app.MapGet("/payee-changes/head", async (PayeesDb db, CancellationToken ct) =>
            Results.Ok(new { position = await db.PayeeChanges.MaxAsync(c => (long?)c.Position, ct) ?? 0 }));
        app.MapGet("/payees/following", async (PayeesDb db, CancellationToken ct) =>
            Results.Ok(await db.FollowPositions.AsNoTracking().ToListAsync(ct)));

        // For the big-bang lab only: every payee, as of now, in one response.
        app.MapGet("/payees/export", async (PayeesDb db, CancellationToken ct) =>
            Results.Ok(await db.Payees.AsNoTracking().Select(p => p.ToContract()).ToListAsync(ct)));

        return app;
    }

    /// <summary>Records every existing payee in the change log, so a follower starting from zero gets them all.</summary>
    internal const string BackfillChanges = """
        INSERT INTO payees.payee_changes (kind, payee_id, customer_account_id, payee_account_id, name, reference, added_at, changed_at)
        SELECT 'Added', payee_id, customer_account_id, payee_account_id, name, reference, added_at, added_at
        FROM payees.payees
        ORDER BY added_at, payee_id
        """;

    /// <summary>Brings the module's tables up to date, then seeds three payees per account if it has none.</summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayeesDb>();
        var seed = scope.ServiceProvider.GetRequiredService<PayeesSeed>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtext({Schema}))", ct);
            await db.Database.MigrateAsync(ct);
            if (!await db.Payees.AnyAsync(ct))
            {
                // Account n pays accounts n+1, n+2 and n+3, wrapping round at the end.
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO payees.payees (payee_id, customer_account_id, payee_account_id, name, reference, added_at)
                    SELECT uuidv7(), a, (a + k - 1) % {seed.Accounts} + 1, 'Customer ' || ((a + k - 1) % {seed.Accounts} + 1), 'Payment', now()
                    FROM generate_series(1, {seed.Accounts}) AS a, generate_series(1, 3) AS k
                    """, ct);
                await db.Database.ExecuteSqlRawAsync(BackfillChanges, ct);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock(hashtext({Schema}))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}

internal sealed record PayeesSeed(int Accounts);

internal sealed record AddPayeeRequest(int PayeeAccountId, string Name, string? Reference);

internal sealed class PayeeRow
{
    public Guid PayeeId { get; set; }
    public int CustomerAccountId { get; set; }
    public int PayeeAccountId { get; set; }
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }

    public Payee ToContract() => new(PayeeId, CustomerAccountId, PayeeAccountId, Name, Reference, AddedAt);
}

internal sealed class PayeesDb(DbContextOptions<PayeesDb> options) : DbContext(options)
{
    public DbSet<PayeeRow> Payees => Set<PayeeRow>();
    public DbSet<PayeeChangeRow> PayeeChanges => Set<PayeeChangeRow>();
    public DbSet<FollowPositionRow> FollowPositions => Set<FollowPositionRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(PayeesModule.Schema);
        model.Entity<PayeeRow>(payee =>
        {
            payee.ToTable("payees");
            payee.HasKey(p => p.PayeeId);
            payee.Property(p => p.Name).HasMaxLength(200);
            payee.Property(p => p.Reference).HasMaxLength(200);
            // A customer saves each account once.
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

internal sealed class PayeeStore(PayeesDb db, TimeProvider time) : IPayees
{
    public Task<Payee?> GetAsync(int customerAccountId, Guid payeeId, CancellationToken ct) =>
        db.Payees.AsNoTracking()
            .Where(p => p.CustomerAccountId == customerAccountId && p.PayeeId == payeeId)
            .Select(p => p.ToContract())
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Payee>> ListAsync(int customerAccountId, CancellationToken ct) =>
        await db.Payees.AsNoTracking()
            .Where(p => p.CustomerAccountId == customerAccountId)
            .OrderBy(p => p.Name).ThenBy(p => p.PayeeId)
            .Select(p => p.ToContract())
            .ToListAsync(ct);

    /// <summary>Saves a payee, or returns null if the customer already has one for that account.</summary>
    public async Task<Payee?> AddAsync(int customerAccountId, AddPayeeRequest request, CancellationToken ct)
    {
        var row = new PayeeRow
        {
            PayeeId = Guid.CreateVersion7(),
            CustomerAccountId = customerAccountId,
            PayeeAccountId = request.PayeeAccountId,
            Name = request.Name.Trim(),
            Reference = request.Reference?.Trim() ?? "",
            AddedAt = time.GetUtcNow(),
        };
        db.Payees.Add(row);
        db.PayeeChanges.Add(PayeeChangeRow.For(ChangeKind.Added, row, row.AddedAt)); // saved together
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
        db.PayeeChanges.Add(PayeeChangeRow.For(ChangeKind.Removed, row, time.GetUtcNow())); // saved together
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // removed by someone else in the meantime
        }
    }
}
