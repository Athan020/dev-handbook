namespace Assessments.Set4;

// Transfers between two different currencies pay an extra percentage. Currency codes are
// compared ignoring case: "zar" and "ZAR" are the same currency.
public sealed class CrossCurrencyFeeRule(decimal rate) : IFeeRule
{
    public decimal FeeFor(TransferRequest request) =>
        string.Equals(request.FromCurrency, request.ToCurrency, StringComparison.OrdinalIgnoreCase) ? 0m : request.Amount * rate;
}
