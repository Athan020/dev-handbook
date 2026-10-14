namespace Bank.Money;

// #region currency
/// <summary>A currency, and how many decimal places ISO 4217 gives it.</summary>
public sealed record Currency(string Code, int MinorUnits)
{
    public static readonly Currency ZAR = new("ZAR", 2);
    public static readonly Currency JPY = new("JPY", 0);
    public static readonly Currency KWD = new("KWD", 3);
}
// #endregion currency

/// <summary>An amount in a currency, never with more decimal places than the currency has.</summary>
public readonly record struct Money
{
    // #region money
    public Money(decimal amount, Currency currency)
    {
        if (decimal.Round(amount, currency.MinorUnits) != amount)
        {
            throw new ArgumentException($"{currency.Code} has {currency.MinorUnits} decimal places, and {amount} has more. Round it first, and say how.");
        }
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public Currency Currency { get; }

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, Same(a, b));
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, Same(a, b));

    // A percentage of an amount has more decimal places than the currency allows, so the
    // caller must choose the rounding rule: there's no safe default.
    public Money Times(decimal factor, MidpointRounding rounding) =>
        new(decimal.Round(Amount * factor, Currency.MinorUnits, rounding), Currency);

    private static Currency Same(Money a, Money b) => a.Currency == b.Currency
        ? a.Currency
        : throw new InvalidOperationException($"Can't add {a.Currency.Code} to {b.Currency.Code}: convert one first.");
    // #endregion money

    // #region allocate
    // Split this amount in proportion to the weights so the parts add up exactly to the whole.
    // Work in minor units (cents): give each part its share rounded down, then hand the cents
    // left over, one each, to the parts that lost most to rounding (the largest remainders),
    // breaking ties in favour of the earlier part.
    public Money[] Allocate(params int[] weights)
    {
        if (weights.Length == 0 || weights.Any(w => w < 0) || weights.Sum() == 0)
        {
            throw new ArgumentException("Weights must be non-negative, and at least one must be positive.");
        }

        var unit = Pow10(Currency.MinorUnits);
        var sign = Math.Sign(Amount);
        var total = (long)(Math.Abs(Amount) * unit);
        var sum = weights.Sum();

        var exact = weights.Select(w => (decimal)total * w / sum).ToArray();
        var shares = exact.Select(e => (long)Math.Floor(e)).ToArray();
        var leftover = total - shares.Sum();

        var order = Enumerable.Range(0, weights.Length).OrderByDescending(i => exact[i] - shares[i]).ThenBy(i => i);
        foreach (var i in order.Take((int)leftover))
        {
            shares[i]++;
        }

        var currency = Currency;
        return [.. shares.Select(s => new Money(sign * s / unit, currency))];
    }
    // #endregion allocate

    private static decimal Pow10(int n) => Enumerable.Repeat(10m, n).Aggregate(1m, (a, b) => a * b);

    public override string ToString() => $"{Amount} {Currency.Code}";
}
