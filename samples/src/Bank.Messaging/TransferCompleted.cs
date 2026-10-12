namespace Bank.Messaging;

/// <summary>A transfer between two accounts has been committed.</summary>
public sealed record TransferCompleted(
    Guid TransferId,
    int FromAccountId,
    int ToAccountId,
    decimal Amount,
    string Reference,
    DateTimeOffset CompletedAt);
