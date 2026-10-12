using System.Data;
using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Api.Endpoints;

/// <summary>
/// Journey B3. Making sure an account has the money it sends. Lab:Funds chooses how:
///   None (the default): no check at all, as in B1 and B2.
///   CheckSum: read the balance (the sum of the account's transactions), check it, then write
///   (stage 1). Lab:Isolation sets the transaction's isolation level.
///   Conditional, ForUpdate, Optimistic: check and change the stored balance (stage 2).
/// Lab:LockOrder=Request (the default) updates the payer's account first; AccountId updates
/// the lower account id first, whatever the direction (stage 2). Lab:LockLate=true changes the
/// balances after writing everything else, to hold the row locks for less time (stage 2).
/// Lab:Retries (default 0) retries a transfer that fails with a serialization failure, a
/// deadlock or an optimistic concurrency conflict, up to that many times.
/// </summary>
public static class Funds
{
    public static IsolationLevel Isolation(IConfiguration config) => config["Lab:Isolation"] switch
    {
        "RepeatableRead" => IsolationLevel.RepeatableRead,
        "Serializable" => IsolationLevel.Serializable,
        _ => IsolationLevel.ReadCommitted, // Postgres's default
    };

    // #region check-sum
    // Stage 1: read the balance, check it, then write. Correct when transfers arrive one at a
    // time. When two arrive together, both can read the same balance before either has written.
    public static async Task<bool> HasFundsAsync(BankDb db, int accountId, decimal amount, CancellationToken ct)
    {
        var balance = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .SumAsync(t => t.Amount, ct);
        return balance >= amount;
    }
    // #endregion check-sum

    /// <summary>Changes both stored balances; false if the payer can't afford it.</summary>
    public static Task<bool> MoveAsync(BankDb db, TransferRequest request, IConfiguration config, CancellationToken ct) =>
        config["Lab:Funds"] switch
        {
            "Conditional" => MoveConditionallyAsync(db, request, config, ct),
            "ForUpdate" => MoveWithRowLocksAsync(db, request, config, ct),
            "Optimistic" => MoveOptimisticallyAsync(db, request, ct),
            _ => Task.FromResult(true),
        };

    public static bool UsesStoredBalance(IConfiguration config) =>
        config["Lab:Funds"] is "Conditional" or "ForUpdate" or "Optimistic";

    // #region lock-order
    // The two balance changes, in the order to lock the rows. Request order locks the payer
    // first, so A-to-B and B-to-A transfers running together can each hold the lock the other
    // needs: a deadlock. Locking the lower account id first, every time, makes that impossible.
    private static (int AccountId, decimal Change)[] InLockOrder(TransferRequest request, IConfiguration config)
    {
        (int, decimal)[] changes = [(request.FromAccountId, -request.Amount), (request.ToAccountId, request.Amount)];
        return config["Lab:LockOrder"] == "AccountId" ? [.. changes.OrderBy(c => c.Item1)] : changes;
    }
    // #endregion lock-order

    // #region conditional
    // One statement checks and changes the balance. It locks the row until commit, so a second
    // transfer from the same account waits here, then re-checks against the new balance.
    private static async Task<bool> MoveConditionallyAsync(BankDb db, TransferRequest request, IConfiguration config, CancellationToken ct)
    {
        foreach (var (accountId, change) in InLockOrder(request, config))
        {
            var updated = await db.Accounts
                .Where(a => a.Id == accountId && (change > 0 || a.Balance >= -change))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.Balance, a => a.Balance + change)
                    .SetProperty(a => a.BalanceVersion, a => a.BalanceVersion + 1), ct);
            if (updated == 0)
            {
                return false; // can't afford it: the caller rolls back anything already changed
            }
        }
        return true;
    }
    // #endregion conditional

    // #region for-update
    // Lock both rows first, then read, decide and write. Any other transaction that wants either
    // row waits at its SELECT until this one commits. FOR NO KEY UPDATE, not FOR UPDATE: the
    // balance isn't a key, and FOR UPDATE would also block other transactions from inserting
    // transaction rows that reference this account (foreign key checks take a KEY SHARE lock).
    private static async Task<bool> MoveWithRowLocksAsync(BankDb db, TransferRequest request, IConfiguration config, CancellationToken ct)
    {
        var changes = InLockOrder(request, config);
        var balances = new Dictionary<int, decimal>();
        foreach (var (accountId, _) in changes)
        {
            balances[accountId] = await db.Accounts
                .FromSql($"SELECT * FROM accounts WHERE id = {accountId} FOR NO KEY UPDATE")
                .Select(a => a.Balance)
                .SingleAsync(ct);
        }

        if (balances[request.FromAccountId] < request.Amount)
        {
            return false;
        }

        foreach (var (accountId, change) in changes)
        {
            await db.Accounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Balance, a => a.Balance + change)
                .SetProperty(a => a.BalanceVersion, a => a.BalanceVersion + 1), ct);
        }
        return true;
    }
    // #endregion for-update

    // #region optimistic
    // Read without locking, and remember the version. Write only if the version is unchanged;
    // if someone else changed the balance in between, throw, and the whole transfer is retried.
    private static async Task<bool> MoveOptimisticallyAsync(BankDb db, TransferRequest request, CancellationToken ct)
    {
        var payer = await db.Accounts
            .Where(a => a.Id == request.FromAccountId)
            .Select(a => new { a.Balance, a.BalanceVersion })
            .SingleAsync(ct);
        if (payer.Balance < request.Amount)
        {
            return false;
        }

        var updated = await db.Accounts
            .Where(a => a.Id == request.FromAccountId && a.BalanceVersion == payer.BalanceVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Balance, payer.Balance - request.Amount)
                .SetProperty(a => a.BalanceVersion, payer.BalanceVersion + 1), ct);
        if (updated == 0)
        {
            throw new OptimisticConflictException();
        }

        await db.Accounts.Where(a => a.Id == request.ToAccountId).ExecuteUpdateAsync(set => set
            .SetProperty(a => a.Balance, a => a.Balance + request.Amount)
            .SetProperty(a => a.BalanceVersion, a => a.BalanceVersion + 1), ct);
        return true;
    }
    // #endregion optimistic

    // #region retryable
    // Serialization failures (40001) and deadlocks (40P01) mean "this transaction lost a race:
    // run it again from the start". Nothing it did was committed.
    // EF Core wraps the database's error, sometimes twice, so look through the whole chain.
    public static bool IsRetryable(Exception e)
    {
        if (e is OptimisticConflictException)
        {
            return true;
        }
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }
        return false;
    }
    // #endregion retryable
}

/// <summary>Another transaction changed the balance after it was read (Lab:Funds=Optimistic).</summary>
public sealed class OptimisticConflictException() : Exception("The balance changed after it was read.");
