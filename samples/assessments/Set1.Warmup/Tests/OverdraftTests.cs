namespace Assessments.Set1;

public class OverdraftTests
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example_breach_on_the_third_transaction() =>
        Assert.Equal(2, Overdraft.FirstBreach(100_00, 50_00, [-80_00, -60_00, -20_00, 500_00]));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example_never_breached() =>
        Assert.Equal(-1, Overdraft.FirstBreach(0, 10_00, [-5_00, 20_00, -25_00]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_transactions() =>
        Assert.Equal(-1, Overdraft.FirstBreach(0, 0, []));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Reaching_the_limit_exactly_is_not_a_breach() =>
        Assert.Equal(-1, Overdraft.FirstBreach(0, 50_00, [-50_00]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_overdraft_allowed() =>
        Assert.Equal(0, Overdraft.FirstBreach(0, 0, [-1]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Totals_beyond_the_range_of_int()
    {
        // Each deposit fits in an int, but the running balance soon doesn't.
        var amounts = Enumerable.Repeat(2_000_000_000L, 5).Append(-9_000_000_000L).Append(-2_000_000_000L).ToArray();
        Assert.Equal(6, Overdraft.FirstBreach(0, 0, amounts));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input()
    {
        var amounts = Enumerable.Repeat(-1L, 100_000).ToArray();
        Assert.Equal(99_999, await TimeLimit.Within(TimeSpan.FromSeconds(2), () => Overdraft.FirstBreach(0, 99_999, amounts)));
    }
}
