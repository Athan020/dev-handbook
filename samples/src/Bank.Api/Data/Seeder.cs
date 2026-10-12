using System.Diagnostics;
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
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO accounts ("HolderName", "Currency", "OpenedAt")
            SELECT
                (ARRAY['Thandi','Sipho','Aisha','Pieter','Lerato','Johan','Naledi','Ravi','Grace','Kabelo'])[1 + floor(random() * 10)::int]
                    || ' ' ||
                (ARRAY['Nkosi','Botha','Patel','Dlamini','Smith','Mokoena','Naidoo','van Wyk','Khumalo','Jacobs'])[1 + floor(random() * 10)::int],
                'ZAR',
                now() - interval '6 years' + random() * interval '3 years'
            FROM generate_series(1, {options.Accounts})
            """, ct);

        // Account ids are drawn from random()^3, so low ids get far more transactions. With the
        // default 100,000 accounts, account 1 gets about 2% of all rows and the busiest 1% of
        // accounts get about 20%. created_at rises with the row number, as it does in an
        // append-only table. Each row is one kind: salary (5%), incoming transfer (10%),
        // outgoing transfer (25%) or spending (60%); only transfers have a counterparty.
        for (long start = 1; start <= options.Transactions; start += options.BatchSize)
        {
            var end = Math.Min(start + options.BatchSize - 1, options.Transactions);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO transactions ("AccountId", "CounterpartyAccountId", "Amount", "Description", "CreatedAt")
                SELECT
                    account_id,
                    CASE WHEN kind IN ('in', 'out') THEN 1 + floor(r_other * {options.Accounts})::int END,
                    CASE kind
                        WHEN 'salary' THEN round((8000 + r_amount * 42000)::numeric, 2)
                        WHEN 'in' THEN round((50 + r_amount * 4950)::numeric, 2)
                        WHEN 'out' THEN -round((50 + r_amount * 4950)::numeric, 2)
                        ELSE -round((5 + r_amount * 1495)::numeric, 2)
                    END,
                    CASE kind
                        WHEN 'salary' THEN 'Salary'
                        WHEN 'in' THEN 'Incoming transfer'
                        WHEN 'out' THEN 'Transfer'
                        ELSE (ARRAY['Card payment','Debit order','ATM withdrawal','Airtime','Groceries','Fuel','Online purchase','Bank fee'])[1 + floor(r_other * 8)::int]
                    END,
                    created_at
                FROM (
                    SELECT
                        account_id,
                        CASE WHEN k < 0.05 THEN 'salary' WHEN k < 0.15 THEN 'in' WHEN k < 0.40 THEN 'out' ELSE 'spend' END AS kind,
                        r_amount, r_other, created_at
                    FROM (
                        -- random() is volatile, so Postgres evaluates it per row and keeps this subquery separate.
                        SELECT
                            1 + floor({options.Accounts} * power(random(), 3))::int AS account_id,
                            random() AS k,
                            random() AS r_amount,
                            random() AS r_other,
                            now() - interval '2 years' + (n::double precision / {options.Transactions}) * interval '2 years' + random() * interval '1 hour' AS created_at
                        FROM generate_series({start}, {end}) AS n
                    ) AS raw
                ) AS generated
                """, ct);
            log.WriteLine($"  {end:N0} / {options.Transactions:N0} transactions");
        }

        await db.Database.ExecuteSqlRawAsync("RESET session_replication_role", ct);
        await db.Database.ExecuteSqlRawAsync("RESET synchronous_commit", ct);
    }
}
