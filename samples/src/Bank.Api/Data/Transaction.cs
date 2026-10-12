namespace Bank.Api.Data;

public sealed class Transaction
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    /// <summary>The other side of a transfer; null for card payments, fees and the like.</summary>
    public int? CounterpartyAccountId { get; set; }
    public Account? CounterpartyAccount { get; set; }

    /// <summary>Signed amount in the account's currency: negative for money out.</summary>
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
