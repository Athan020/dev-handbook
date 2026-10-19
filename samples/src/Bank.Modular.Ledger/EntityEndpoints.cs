using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Bank.Modular.Ledger;

internal sealed record BalanceUpdate(decimal Balance);

internal sealed record EntryCreate(Guid TransferId, int AccountId, decimal Amount, string Reference);

internal sealed record BooksCheck(int Accounts, decimal TotalBalance, decimal ExpectedTotal, decimal EntriesNet, int AccountsDisagreeing);

internal static class EntityEndpoints
{
    // #region entity-split
    // B5 stage 2 (Lab:EntityServices=true): the ledger split by table, as an Account service
    // and a Transaction service would offer it. Each endpoint does one thing to one table; the
    // caller composes them into a transfer.
    public static void Map(IEndpointRouteBuilder app)
    {
        // The "Account service": read an account, set its balance.
        app.MapGet("/entity/accounts/{accountId:int}", async (int accountId, LedgerDb db, CancellationToken ct) =>
            await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId, ct) is { } account
                ? Results.Ok(new { account.AccountId, account.Balance })
                : Results.NotFound());

        app.MapPut("/entity/accounts/{accountId:int}/balance", async (int accountId, BalanceUpdate update, LedgerDb db, CancellationToken ct) =>
            await db.Accounts.Where(a => a.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, update.Balance), ct) > 0
                ? Results.NoContent()
                : Results.NotFound());

        // The "Transaction service": record an entry.
        app.MapPost("/entity/entries", async (EntryCreate entry, LedgerDb db, TimeProvider time, CancellationToken ct) =>
        {
            db.Entries.Add(new Entry
            {
                TransferId = entry.TransferId, AccountId = entry.AccountId, Amount = entry.Amount,
                Reference = entry.Reference, PostedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            return Results.Created();
        });
    }
    // #endregion entity-split

    // #region books-check
    /// <summary>
    /// Do the books add up? Every account opened with the same balance, and since then only
    /// transfers have changed balances, each posting entries that net to zero. So the total
    /// must be unchanged, and each balance must equal its opening balance plus its entries.
    /// </summary>
    public static Task<BooksCheck> CheckAsync(LedgerDb db, CancellationToken ct) =>
        db.Database.SqlQuery<BooksCheck>($"""
            SELECT (SELECT count(*)::int FROM ledger.accounts) AS accounts,
                   (SELECT coalesce(sum(balance), 0) FROM ledger.accounts) AS total_balance,
                   (SELECT count(*) * {LedgerSeed.OpeningBalance} FROM ledger.accounts) AS expected_total,
                   (SELECT coalesce(sum(amount), 0) FROM ledger.entries) AS entries_net,
                   (SELECT count(*)::int FROM ledger.accounts a
                    WHERE a.balance <> {LedgerSeed.OpeningBalance}
                        + coalesce((SELECT sum(e.amount) FROM ledger.entries e WHERE e.account_id = a.account_id), 0)) AS accounts_disagreeing
            """).SingleAsync(ct);
    // #endregion books-check
}
