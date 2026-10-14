using Bank.Money;
using MoneyAmount = Bank.Money.Money;

namespace Bank.Api.Tests;

/// <summary>B3 stage 4: representing, rounding and splitting money without losing cents.</summary>
public sealed class MoneyTests
{
    private static MoneyAmount Zar(decimal amount) => new(amount, Currency.ZAR);

    [Fact]
    public void Binary_floating_point_drifts_and_decimal_does_not()
    {
        double asDouble = 0;
        decimal asDecimal = 0;
        for (var i = 0; i < 1_000_000; i++)
        {
            asDouble += 0.01;
            asDecimal += 0.01m;
        }

        Assert.NotEqual(10_000.0, asDouble);
        Assert.Equal(10_000m, asDecimal);
    }

    [Fact]
    public void An_amount_with_more_decimal_places_than_the_currency_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Zar(1.005m));
        Assert.Throws<ArgumentException>(() => new MoneyAmount(10.5m, Currency.JPY));
        Assert.Equal(1.005m, new MoneyAmount(1.005m, Currency.KWD).Amount);
    }

    [Theory]
    [InlineData(MidpointRounding.AwayFromZero, 2.67)]
    [InlineData(MidpointRounding.ToEven, 2.66)]
    public void Rounding_a_fee_needs_an_explicit_rule(MidpointRounding rounding, decimal fee) =>
        Assert.Equal(fee, Zar(533.00m).Times(0.005m, rounding).Amount);

    [Fact]
    public void Different_currencies_cannot_be_added() =>
        Assert.Throws<InvalidOperationException>(() => Zar(10m) + new MoneyAmount(10m, Currency.JPY));

    [Theory]
    [InlineData(0.05, new[] { 50, 30, 20 }, new[] { 0.03, 0.01, 0.01 })]
    [InlineData(100.00, new[] { 1, 1, 1 }, new[] { 33.34, 33.33, 33.33 })]
    [InlineData(-0.05, new[] { 50, 30, 20 }, new[] { -0.03, -0.01, -0.01 })]
    [InlineData(0.01, new[] { 1, 1, 1 }, new[] { 0.01, 0, 0 })]
    public void Allocating_gives_parts_that_add_up(double amount, int[] weights, double[] expected)
    {
        var parts = Zar((decimal)amount).Allocate(weights);

        Assert.Equal(expected.Select(e => (decimal)e), parts.Select(p => p.Amount));
        Assert.Equal((decimal)amount, parts.Sum(p => p.Amount));
    }

    [Fact]
    public void Allocating_yen_works_in_whole_yen() =>
        Assert.Equal([334m, 333m, 333m], new MoneyAmount(1_000m, Currency.JPY).Allocate(1, 1, 1).Select(p => p.Amount));

    [Fact]
    public void Every_allocation_adds_up_and_each_part_is_within_one_cent_of_its_exact_share()
    {
        var random = new Random(4);
        for (var round = 0; round < 10_000; round++)
        {
            var whole = Zar(random.Next(0, 10_000_000) / 100m);
            int[] weights = [.. Enumerable.Range(0, random.Next(1, 6)).Select(_ => random.Next(1, 100))];

            var parts = whole.Allocate(weights);

            Assert.Equal(whole.Amount, parts.Sum(p => p.Amount));
            for (var i = 0; i < weights.Length; i++)
            {
                var exact = whole.Amount * weights[i] / weights.Sum();
                Assert.True(Math.Abs(parts[i].Amount - exact) < 0.01m, $"{parts[i]} is more than a cent from {exact}");
            }
        }
    }
}
