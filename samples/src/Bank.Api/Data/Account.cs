namespace Bank.Api.Data;

public sealed class Account
{
    public int Id { get; set; }
    public required string HolderName { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>
    /// B2 stage 5: how many events this account has had. Each new event takes the next number,
    /// so consumers can tell when events arrive out of order. 1 is "account opened".
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// B3 stage 2: the balance, stored, so a transfer can check and change it in one statement.
    /// It must equal the sum of the account's transactions; stage 5 asks what happens when it doesn't.
    /// Only the stage 2 transfer modes (Lab:Funds=Conditional, ForUpdate or Optimistic) and
    /// deposits keep it up to date.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>B3 stage 2: changes whenever the balance does, for optimistic concurrency.</summary>
    public int BalanceVersion { get; set; }
}
