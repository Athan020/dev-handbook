using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Data;

/// <summary>
/// Fills the database with realistic, deterministic data: a few accounts with very many
/// transactions and many accounts with few, the way real banking data is skewed.
/// The rows are generated inside Postgres with set-based SQL, which is far faster than
/// inserting them from .NET.
/// </summary>
public static class Seeder
{
    public sealed record Options(int Accounts = 100_000, long Transactions = 10_000_000, long BatchSize = 1_000_000);

    public static async Task SeedAsync(BankDb db, Options options, TextWriter log, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();

        await db.Database.MigrateAsync(ct);
        if (await db.Accounts.AnyAsync(ct))
        {
            log.WriteLine("The database already has data. To reseed, run `docker compose down -v` and start again.");
            return;
        }

        // Everything below must run on one connection: setseed() and SET only last for the
        // session, and EF would otherwise take a connection from the pool for each command.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await InsertRowsAsync(db, options, log, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        log.WriteLine("Updating planner statistics (ANALYZE)...");
        await db.Database.ExecuteSqlRawAsync("ANALYZE", ct);
        log.WriteLine($"Done in {timer.Elapsed.TotalSeconds:N0}s.");
    }

    private static async Task InsertRowsAsync(BankDb db, Options options, TextWriter log, CancellationToken ct)
    {
        // Same seed, same data: every reader sees the same accounts and query plans.
        await db.Database.ExecuteSqlRawAsync("SELECT setseed(0.42)", ct);

        // Speed-ups for this session only. The generated ids are always valid, so skip the
        // per-row foreign key checks (Postgres runs them as triggers; this needs a superuser,
        // which the lab database user is). The constraints stay defined for later writes.
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica", ct);
        await db.Database.ExecuteSqlRawAsync("SET synchronous_commit = off", ct);

        log.WriteLine($"Creating {options.Accounts:N0} accounts...");
        await db.Database.ExecuteSqlRawAsync(Fill(ReadSql("accounts.sql"), options), ct);

        var transactionsSql = ReadSql("transactions.sql");
        for (long start = 1; start <= options.Transactions; start += options.BatchSize)
        {
            var end = Math.Min(start + options.BatchSize - 1, options.Transactions);
            await db.Database.ExecuteSqlRawAsync(Fill(transactionsSql, options, start, end), ct);
            log.WriteLine($"  {end:N0} / {options.Transactions:N0} transactions");
        }

        await db.Database.ExecuteSqlRawAsync("RESET session_replication_role", ct);
        await db.Database.ExecuteSqlRawAsync("RESET synchronous_commit", ct);
    }

    // The seed SQL lives in Data/Seed/*.sql, shared with the handbook's in-browser playground.
    // Placeholders are only ever replaced with numbers, so there's nothing to inject.
    private static string ReadSql(string name)
    {
        using var stream = typeof(Seeder).Assembly.GetManifestResourceStream($"Bank.Api.Data.Seed.{name}")
            ?? throw new InvalidOperationException($"Embedded seed script {name} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Fill(string sql, Options options, long start = 0, long end = 0) => sql
        .Replace("{accounts}", options.Accounts.ToString(CultureInfo.InvariantCulture))
        .Replace("{transactions}", options.Transactions.ToString(CultureInfo.InvariantCulture))
        .Replace("{start}", start.ToString(CultureInfo.InvariantCulture))
        .Replace("{end}", end.ToString(CultureInfo.InvariantCulture));
}
