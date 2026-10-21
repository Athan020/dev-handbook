using Bank.Modular.Contracts.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Bank.Modular.Ledger;

internal enum TransferOutcome { Completed, InsufficientFunds, UnknownAccount, AlreadyFinished }

/// <summary>Moves money between two accounts: both balances and both entries, or nothing.</summary>
internal sealed class Transfers(LedgerDb db, TimeProvider time)
{
    /// <summary>
    /// Moves the money. <paramref name="alongside"/>, if given, runs in the same transaction,
    /// after the balance is checked and before anything is saved; if it returns false, nothing
    /// is saved. Accepted transfers and sagas use it to record their progress with the money.
    /// </summary>
    public async Task<(TransferOutcome Outcome, Guid TransferId)> MoveAsync(
        int fromAccountId, int toAccountId, decimal amount, string reference, CancellationToken ct,
        Guid? transferId = null, Func<CancellationToken, Task<bool>>? alongside = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Lock both rows in account order, so two opposite transfers can't deadlock (B3 stage 2).
        // Untracked: a context that has seen these accounts before would otherwise hand back its
        // remembered balances instead of the ones just locked.
        var accounts = await db.Accounts
            .FromSql($"SELECT * FROM ledger.accounts WHERE account_id IN ({fromAccountId}, {toAccountId}) ORDER BY account_id FOR NO KEY UPDATE")
            .AsNoTracking()
            .ToDictionaryAsync(a => a.AccountId, ct);

        if (!accounts.TryGetValue(fromAccountId, out var from) || !accounts.ContainsKey(toAccountId))
            return (TransferOutcome.UnknownAccount, Guid.Empty);
        if (from.Balance < amount)
            return (TransferOutcome.InsufficientFunds, Guid.Empty);
        if (alongside is not null && !await alongside(ct))
            return (TransferOutcome.AlreadyFinished, transferId ?? Guid.Empty);

        await db.Database.ExecuteSqlAsync($"""
            UPDATE ledger.accounts
            SET balance = balance + CASE WHEN account_id = {fromAccountId} THEN -{amount} ELSE {amount} END
            WHERE account_id IN ({fromAccountId}, {toAccountId})
            """, ct);
        var id = transferId ?? Guid.CreateVersion7();
        var now = time.GetUtcNow();
        db.Entries.AddRange(
            new Entry { TransferId = id, AccountId = fromAccountId, Amount = -amount, Reference = reference, PostedAt = now },
            new Entry { TransferId = id, AccountId = toAccountId, Amount = amount, Reference = reference, PostedAt = now });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (TransferOutcome.Completed, id);
    }
}

/// <summary>The ledger's side of its contract with other modules.</summary>
internal sealed class LedgerService(LedgerDb db, TimeProvider time) : ILedger
{
    /// <summary>How long a gap in positions may stay open before the feed treats it as permanent.</summary>
    internal static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(5);

    public Task<AccountView?> GetAccountAsync(int accountId, CancellationToken ct) =>
        db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => new AccountView(a.AccountId, a.HolderName, a.Balance))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(int accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Entries.AsNoTracking()
            .Where(e => e.AccountId == accountId && e.PostedAt >= from && e.PostedAt < to)
            .OrderBy(e => e.Position)
            .Select(e => new LedgerEntry(e.Position, e.TransferId, e.AccountId, e.Amount, e.Reference, e.PostedAt))
            .ToListAsync(ct);

    // #region feed
    public async Task<IReadOnlyList<LedgerEntry>> ReadFeedAsync(long position, int max, CancellationToken ct)
    {
        var rows = await db.Entries.AsNoTracking()
            .Where(e => e.Position > position)
            .OrderBy(e => e.Position)
            .Take(max)
            .Select(e => new LedgerEntry(e.Position, e.TransferId, e.AccountId, e.Amount, e.Reference, e.PostedAt))
            .ToListAsync(ct);

        // Positions are handed out when a transfer inserts its entries, not when it commits, so
        // entry 10 can be visible while entry 9's transfer is still committing. A reader that
        // took 10 and moved its position past 9 would never see 9. So stop at a gap, unless the
        // gap is old enough that it can only be a position a rolled-back transfer never used.
        var feed = new List<LedgerEntry>(rows.Count);
        var expected = position + 1;
        foreach (var row in rows)
        {
            if (row.Position != expected && time.GetUtcNow() - row.PostedAt < GapTimeout) break;
            feed.Add(row);
            expected = row.Position + 1;
        }
        return feed;
    }
    // #endregion feed
}
