namespace Assessments.Set1;

public class DuplicatePaymentsTests
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example()
    {
        Payment[] payments =
        [
            new("p1", "alice", "bob", 25_00, 1_000),
            new("p2", "alice", "bob", 25_00, 1_030),   // 30 s after p1: a duplicate
            new("p3", "alice", "carol", 25_00, 1_040), // different payee
            new("p4", "alice", "bob", 25_00, 1_200),   // 170 s after p2
        ];
        Assert.Equal(["p2"], DuplicatePayments.Find(payments));
    }

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Different_amounts_are_not_duplicates() =>
        Assert.Empty(DuplicatePayments.Find([new("a", "x", "y", 10_00, 0), new("b", "x", "y", 10_01, 1)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_payments() =>
        Assert.Empty(DuplicatePayments.Find([]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Exactly_sixty_seconds_is_a_duplicate_and_sixty_one_is_not()
    {
        Payment[] payments = [new("a", "x", "y", 1, 0), new("b", "x", "y", 1, 60), new("c", "x", "y", 1, 121)];
        Assert.Equal(["b"], DuplicatePayments.Find(payments));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Same_second() =>
        Assert.Equal(["b", "c"], DuplicatePayments.Find([new("a", "x", "y", 1, 5), new("b", "x", "y", 1, 5), new("c", "x", "y", 1, 5)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void A_chain_counts_from_the_previous_payment_not_the_first()
    {
        Payment[] payments = [new("a", "x", "y", 1, 0), new("b", "x", "y", 1, 50), new("c", "x", "y", 1, 100)];
        Assert.Equal(["b", "c"], DuplicatePayments.Find(payments));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Payer_and_payee_are_not_interchangeable() =>
        Assert.Empty(DuplicatePayments.Find([new("a", "x", "y", 1, 0), new("b", "y", "x", 1, 1)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_in_a_busy_minute()
    {
        // 200,000 different payments within one minute: comparing each with every earlier one is too slow.
        var payments = Enumerable.Range(0, 200_000)
            .Select(i => new Payment($"p{i}", $"payer{i}", "merchant", 10_00, i / 4_000))
            .ToArray();
        var found = await TimeLimit.Within(TimeSpan.FromSeconds(2), () => DuplicatePayments.Find(payments));
        Assert.Empty(found);
    }
}
