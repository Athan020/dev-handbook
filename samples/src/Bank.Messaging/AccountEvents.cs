namespace Bank.Messaging;

/// <summary>B2 stage 5: an account was opened. Always sequence 1 for that account.</summary>
public sealed record AccountOpened(int AccountId, string HolderName, int Sequence, DateTimeOffset OpenedAt);

/// <summary>B2 stage 5: money was posted to an account, as its <see cref="Sequence"/>th event.</summary>
public sealed record AccountEntryPosted(int AccountId, Guid TransferId, decimal Amount, int Sequence, DateTimeOffset PostedAt);
