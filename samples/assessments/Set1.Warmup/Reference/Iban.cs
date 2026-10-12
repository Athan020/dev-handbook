namespace Assessments.Set1;

// Worked solution. The rearranged IBAN can be a 60-digit number, far beyond long or decimal,
// so compute the remainder one digit at a time instead of building the number.
public static class Iban
{
    public static bool IsValid(string iban)
    {
        var compact = iban.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (compact.Length is < 15 or > 34
            || !char.IsAsciiLetterUpper(compact[0]) || !char.IsAsciiLetterUpper(compact[1])
            || !char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3])
            || !compact.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        var rearranged = compact[4..] + compact[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            // A letter stands for two digits (A = 10 ... Z = 35); a digit for itself.
            var value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value < 10
                ? (remainder * 10 + value) % 97
                : (remainder * 100 + value) % 97;
        }

        return remainder == 1;
    }
}
