namespace Assessments.Set4;

// Every transfer pays a percentage of its amount, with a minimum fee.
public sealed class PercentageFeeRule(decimal rate, decimal minimum) : IFeeRule
{
    public decimal FeeFor(TransferRequest request) => Math.Max(request.Amount * rate, minimum);
}
