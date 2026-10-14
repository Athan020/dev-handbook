namespace Bank.Api.Data;

/// <summary>
/// B3 stage 6: a payment to another bank. Its id is the reference the payment network sees,
/// so asking the network "did this happen?" and sending it again both use the same identity.
/// </summary>
public sealed class OutgoingPayment
{
    public Guid Id { get; set; }
    public int FromAccountId { get; set; }
    public decimal Amount { get; set; }
    public required string ToBank { get; set; }
    public required string ToAccount { get; set; }

    /// <summary>Pending until the network's answer is known; then Settled or Failed.</summary>
    public required string Status { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
