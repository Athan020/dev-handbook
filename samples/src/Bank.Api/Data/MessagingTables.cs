namespace Bank.Api.Data;

/// <summary>
/// B2 stage 3: points awarded for a transfer, by the rewards service. A side effect in the
/// database, so duplicates are easy to see.
/// </summary>
public sealed class RewardPoint
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public Guid TransferId { get; set; }
    public int Points { get; set; }
    public DateTimeOffset AwardedAt { get; set; }
}

/// <summary>
/// B2 stage 3: messages a consumer has already handled (an "inbox"). Recording the message id
/// in the same transaction as the side effect makes handling a duplicate a no-op.
/// </summary>
public sealed class ProcessedMessage
{
    public required string Consumer { get; set; }
    public required string MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>
/// B2 stage 4: events waiting to be published (the transactional outbox). Written in the same
/// transaction as the change they describe; a relay publishes them and marks them sent.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
