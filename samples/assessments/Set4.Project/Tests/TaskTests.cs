namespace Assessments.Set4;

// Tests for the task: the two reported bugs and the premium cap.
public class TaskTests
{
    private static decimal Fee(decimal amount, string from = "ZAR", string to = "ZAR", CustomerTier tier = CustomerTier.Standard) =>
        FeeCalculator.Default.Calculate(new TransferRequest(amount, from, to, tier));

    // Bug 1: "A transfer of 533.00 was charged 2.66. Our tariff says 0.5%, halves rounded up: 2.67."
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Bug1_halves_round_up() => Assert.Equal(2.67m, Fee(533m));

    [Theory, Trait(Tests.Category, Tests.Hidden)]
    [InlineData(1_001, 5.01)]    // 5.005
    [InlineData(1_003, 5.02)]    // 5.015
    [InlineData(200.50, 1.00)]   // 1.0025: the minimum applies
    [InlineData(2_000.99, 10.00)] // 10.00495: rounds down
    public void Bug1_hidden_rounding(decimal amount, decimal fee) => Assert.Equal(fee, Fee(amount));

    // Bug 2: "A transfer from a ZAR account to a zar account was charged the cross-currency fee."
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Bug2_currency_codes_ignore_case() => Assert.Equal(5.00m, Fee(1_000m, "ZAR", "zar"));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Bug2_hidden_lower_case_on_both_sides_still_differ() => Assert.Equal(15.00m, Fee(1_000m, "zar", "usd"));

    // Feature: "Premium customers' total fee is capped at 25.00. Standard customers have no cap."
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Feature_premium_fee_is_capped() => Assert.Equal(25.00m, Fee(10_000m, "ZAR", "USD", CustomerTier.Premium));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Feature_standard_fee_is_not_capped() => Assert.Equal(150.00m, Fee(10_000m, "ZAR", "USD"));

    [Theory, Trait(Tests.Category, Tests.Hidden)]
    [InlineData(5_000, "ZAR", 25.00)]   // exactly at the cap
    [InlineData(4_000, "ZAR", 20.00)]   // below the cap
    [InlineData(100, "ZAR", 1.00)]      // the minimum still applies
    [InlineData(1_667, "USD", 25.00)]   // 25.005 before rounding: capped, not rounded up past the cap
    public void Feature_hidden_premium(decimal amount, string to, decimal fee) =>
        Assert.Equal(fee, Fee(amount, "ZAR", to, CustomerTier.Premium));
}
