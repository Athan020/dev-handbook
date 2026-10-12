namespace Assessments.Set4;

public sealed class FeeCalculator(IReadOnlyList<IFeeRule> rules)
{
    public const decimal PremiumCap = 25.00m;

    // The bank's current tariff.
    public static FeeCalculator Default { get; } = new(
    [
        new PercentageFeeRule(rate: 0.005m, minimum: 1.00m),
        new CrossCurrencyFeeRule(rate: 0.01m),
    ]);

    // The total fee for a transfer, rounded to the nearest cent, halves away from zero.
    // Premium customers never pay more than PremiumCap.
    public decimal Calculate(TransferRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);

        var total = Math.Round(rules.Sum(rule => rule.FeeFor(request)), 2, MidpointRounding.AwayFromZero);
        return request.Tier == CustomerTier.Premium ? Math.Min(total, PremiumCap) : total;
    }
}
