namespace Bank.Api.Data;

/// <summary>
/// B4 stage 5: a transfer that went ahead, or was held, without a fraud decision, because the
/// fraud service was unavailable. Unchecked transfers are reviewed afterwards; held ones wait
/// until the fraud service can decide.
/// </summary>
public sealed class FraudReview
{
    public Guid Id { get; set; }
    public int FromAccountId { get; set; }
    public int ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public required string Reference { get; set; }

    /// <summary>Unchecked (made without a decision), Held, Released (approved and made) or Declined.</summary>
    public required string Status { get; set; }

    public Guid? TransferId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
