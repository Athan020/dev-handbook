using Bank.Modular.Contracts.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Modular.Statements;

/// <summary>The statements module: an account's monthly statement. Its only public type.</summary>
public static class StatementsModule
{
    public const string Schema = "statements";

    public static IServiceCollection AddStatementsModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<StatementsDb>(options => options
            .UseNpgsql(config.GetConnectionString("Statements") ?? config.GetConnectionString("Modular"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<StatementBuilder>();

        // #region statements-source
        // B5 stage 3: Lab:Statements=Call asks the ledger for every statement; Copy keeps a copy
        // of the ledger's entries, following its feed, and builds statements from that.
        var fromCopy = config["Lab:Statements"] == "Copy";
        services.AddSingleton(new StatementsSource(fromCopy));
        if (fromCopy)
        {
            services.AddSingleton(new CopyOptions(
                TimeSpan.FromMilliseconds(config.GetValue("Statements:FeedIntervalMs", 1000)),
                config.GetValue("Statements:FeedPageSize", 500)));
            services.AddHostedService<LedgerFeedFollower>();
        }
        // #endregion statements-source
        return services;
    }

    public static IEndpointRouteBuilder MapStatements(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{accountId:int}/statements/{year:int}/{month:int}",
            async (int accountId, int year, int month, StatementBuilder builder, CancellationToken ct) =>
                month is < 1 or > 12
                    ? Results.Problem(statusCode: 400, title: "The month must be 1 to 12.")
                    : Results.Ok(await builder.BuildAsync(accountId, year, month, ct)));

        app.MapGet("/statements/copy", async (StatementsDb db, CancellationToken ct) =>
            Results.Ok(await db.FeedPositions.AsNoTracking().SingleAsync(p => p.Feed == LedgerFeedFollower.Feed, ct)));
        return app;
    }

    /// <summary>Creates the module's tables if they don't exist yet: the copy, and where it got to.</summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatementsDb>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({Schema}))", ct);
        var exists = await db.Database.SqlQuery<bool>($"SELECT to_regclass('statements.lines') IS NOT NULL AS \"Value\"").SingleAsync(ct);
        if (!exists)
        {
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript(), ct);
            db.FeedPositions.Add(new FeedPosition { Feed = LedgerFeedFollower.Feed });
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}

internal sealed record StatementsSource(bool FromCopy);

internal sealed record StatementLine(Guid TransferId, DateTimeOffset PostedAt, string Reference, decimal Amount);

/// <summary>A month's statement, complete as of <see cref="AsOf"/>.</summary>
internal sealed record Statement(int AccountId, int Year, int Month, decimal MoneyIn, decimal MoneyOut, DateTimeOffset AsOf, IReadOnlyList<StatementLine> Lines);

internal sealed class StatementBuilder(StatementsSource source, ILedger ledger, StatementsDb db, TimeProvider time)
{
    // #region build
    public async Task<Statement> BuildAsync(int accountId, int year, int month, CancellationToken ct)
    {
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);

        List<StatementLine> lines;
        DateTimeOffset asOf;
        if (source.FromCopy)
        {
            // Our own copy: available and fast whatever the ledger is doing, but only as fresh as
            // the last time the copy caught up, so say so.
            asOf = (await db.FeedPositions.AsNoTracking().SingleAsync(p => p.Feed == LedgerFeedFollower.Feed, ct)).CheckedAt;
            lines = await db.Lines.AsNoTracking()
                .Where(l => l.AccountId == accountId && l.PostedAt >= from && l.PostedAt < to)
                .OrderBy(l => l.Position)
                .Select(l => new StatementLine(l.TransferId, l.PostedAt, l.Reference, l.Amount))
                .ToListAsync(ct);
        }
        else
        {
            // Ask the owner: always current, but only while the ledger is there to answer.
            asOf = time.GetUtcNow();
            lines = (await ledger.EntriesAsync(accountId, from, to, ct))
                .Select(e => new StatementLine(e.TransferId, e.PostedAt, e.Reference, e.Amount))
                .ToList();
        }

        return new Statement(accountId, year, month,
            MoneyIn: lines.Where(l => l.Amount > 0).Sum(l => l.Amount),
            MoneyOut: -lines.Where(l => l.Amount < 0).Sum(l => l.Amount),
            asOf, lines);
    }
    // #endregion build
}
