namespace Assessments.Set4;

public sealed class FeeCalculator(IReadOnlyList<IFeeRule> rules)
{
    // The bank's current tariff.
    public static FeeCalculator Default { get; } = new(
    [
        new PercentageFeeRule(rate: 0.005m, minimum: 1.00m),
        new CrossCurrencyFeeRule(rate: 0.01m),
    ]);

    // The total fee for a transfer, rounded to the nearest cent.
    public decimal Calculate(TransferRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);

        var total = rules.Sum(rule => rule.FeeFor(request));
        return Math.Round(total, 2);
    }
}
