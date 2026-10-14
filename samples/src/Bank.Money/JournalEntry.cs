namespace Bank.Money;

/// <summary>One line of a journal entry: an amount into (positive) or out of (negative) an account.</summary>
public sealed record Posting(string AccountId, Money Amount);

// #region journal-entry
/// <summary>
/// B3 stage 5: one movement of money, as postings that add up to zero. It can't be built
/// unbalanced, and it's never changed afterwards: a mistake is corrected by a new entry.
/// </summary>
public sealed class JournalEntry
{
    public JournalEntry(string description, IReadOnlyList<Posting> postings, Guid? corrects = null)
    {
        if (postings.Count < 2)
        {
            throw new ArgumentException("An entry needs at least two postings: money comes from somewhere and goes somewhere.");
        }
        if (postings.Any(p => p.Amount.Amount == 0))
        {
            throw new ArgumentException("A posting of zero records nothing.");
        }
        if (postings.Select(p => p.Amount.Currency).Distinct().Count() != 1)
        {
            throw new ArgumentException("All postings in an entry must be in one currency; record a conversion as its own entries.");
        }
        if (postings.Sum(p => p.Amount.Amount) != 0)
        {
            throw new ArgumentException($"The postings add up to {postings.Sum(p => p.Amount.Amount)}, not zero.");
        }

        Description = description;
        Postings = postings;
        Corrects = corrects;
    }

    public Guid Id { get; } = Guid.CreateVersion7();
    public string Description { get; }
    public IReadOnlyList<Posting> Postings { get; }

    /// <summary>The entry this one corrects, if any.</summary>
    public Guid? Corrects { get; }

    // Undo an entry by posting its opposite, leaving both in the history.
    public JournalEntry Reverse(string reason) => new(
        reason,
        [.. Postings.Select(p => p with { Amount = new Money(-p.Amount.Amount, p.Amount.Currency) })],
        corrects: Id);
}
// #endregion journal-entry
