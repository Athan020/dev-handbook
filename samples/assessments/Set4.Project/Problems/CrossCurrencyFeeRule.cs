namespace Assessments.Set4;

// Transfers between two different currencies pay an extra percentage.
public sealed class CrossCurrencyFeeRule(decimal rate) : IFeeRule
{
    public decimal FeeFor(TransferRequest request) =>
        request.FromCurrency == request.ToCurrency ? 0m : request.Amount * rate;
}
