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

    /// <summary>Strict insertion order, even for rows written in the same transaction (stage 5).</summary>
    public long Position { get; set; }

    /// <summary>Where the relay publishes it.</summary>
    public string Exchange { get; set; } = "transfers";
    public string RoutingKey { get; set; } = "transfer.completed";

    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>B2 stage 5: the statements service's view of one account, built from account events.</summary>
public sealed class StatementAccount
{
    public int AccountId { get; set; }
    public int LastSequence { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>B2 stage 5: every event the statements service received in the wrong order.</summary>
public sealed class StatementProblem
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public int Sequence { get; set; }
    public required string Problem { get; set; }
    public DateTimeOffset At { get; set; }
}
