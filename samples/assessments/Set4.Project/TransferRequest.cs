namespace Assessments.Set4;

public enum CustomerTier
{
    Standard,
    Premium,
}

// A transfer to be priced. Currencies are ISO 4217 codes such as "ZAR" or "USD".
public sealed record TransferRequest(decimal Amount, string FromCurrency, string ToCurrency, CustomerTier Tier);
