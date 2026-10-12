namespace Bank.Api.Data;

/// <summary>
/// B3 stage 3: a client's idempotency key, and the response it was given. The key is unique
/// per payer (the customer making the request), so two customers' keys can't collide.
/// </summary>
public sealed class IdempotencyKey
{
    public int PayerAccountId { get; set; }
    public required string Key { get; set; }

    /// <summary>A hash of the request, so the same key sent with a different request is caught.</summary>
    public required string RequestHash { get; set; }

    public int? StatusCode { get; set; }
    public string? Response { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
