namespace Bank.Modular.Contracts.Payees;

/// <summary>What the payees module offers other modules: the people a customer pays.</summary>
public interface IPayees
{
    /// <summary>One of the customer's saved payees, or null if they have no such payee.</summary>
    Task<Payee?> GetAsync(int customerAccountId, Guid payeeId, CancellationToken ct);
}

public sealed record Payee(Guid PayeeId, int CustomerAccountId, int PayeeAccountId, string Name, string Reference, DateTimeOffset AddedAt);
