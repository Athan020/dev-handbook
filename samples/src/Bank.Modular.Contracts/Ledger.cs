namespace Bank.Modular.Contracts.Ledger;

// #region ledger-contract
/// <summary>
/// What the ledger module offers other modules. Accounts, balances and entries are the
/// ledger's own data: other modules ask through here, and never read its tables.
/// </summary>
public interface ILedger
{
    Task<AccountView?> GetAccountAsync(int accountId, CancellationToken ct);

    /// <summary>An account's entries posted in [from, to), oldest first.</summary>
    Task<IReadOnlyList<LedgerEntry>> EntriesAsync(int accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    /// The ledger's feed: every entry, in the order posted, after <paramref name="position"/>.
    /// A module that keeps its own copy reads this and remembers where it got to.
    /// </summary>
    Task<IReadOnlyList<LedgerEntry>> ReadFeedAsync(long position, int max, CancellationToken ct);
}

public sealed record AccountView(int AccountId, string HolderName, decimal Balance);

public sealed record LedgerEntry(long Position, Guid TransferId, int AccountId, decimal Amount, string Reference, DateTimeOffset PostedAt);
// #endregion ledger-contract
