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
/// Lab:Retries (default 0) retries a transfer that fails with a serialization failure or a
/// deadlock, up to that many times.
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

    // #region retryable
    // Serialization failures (40001) and deadlocks (40P01) mean "this transaction lost a race:
    // run it again from the start". Nothing it did was committed.
    // EF Core wraps the database's error, sometimes twice, so look through the whole chain.
    public static bool IsRetryable(Exception e)
    {
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
