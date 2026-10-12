namespace Assessments.Set4;

// The codebase's own tests, as you'd find them in the repository.
public class ExistingTests
{
    private static decimal Fee(decimal amount, string from = "ZAR", string to = "ZAR", CustomerTier tier = CustomerTier.Standard) =>
        FeeCalculator.Default.Calculate(new TransferRequest(amount, from, to, tier));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Percentage_fee() => Assert.Equal(5.00m, Fee(1_000m));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Minimum_fee_for_small_transfers() => Assert.Equal(1.00m, Fee(50m));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Cross_currency_fee_is_added() => Assert.Equal(15.00m, Fee(1_000m, "ZAR", "USD"));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Amount_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fee(0m));
}
