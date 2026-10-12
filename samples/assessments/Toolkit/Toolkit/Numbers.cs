namespace Assessments.Toolkit;

public static class Numbers
{
    public const long Modulus = 1_000_000_007;

    // #region checked
    // Integer overflow is silent by default. A checked block throws instead, which turns a
    // wrong answer into an exception you can see while testing.
    public static long TotalChecked(IEnumerable<long> amounts)
    {
        long total = 0;
        foreach (var amount in amounts)
        {
            total = checked(total + amount);
        }
        return total;
    }
    // #endregion

    // #region mod-pow
    // base^exponent mod Modulus in O(log exponent), by repeated squaring. Every product of two
    // values below Modulus fits in a long.
    public static long ModPow(long value, long exponent)
    {
        long result = 1;
        value %= Modulus;
        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result = result * value % Modulus;
            }
            value = value * value % Modulus;
            exponent >>= 1;
        }
        return result;
    }
    // #endregion

    // #region floor-mod
    // In C#, % takes the sign of the left operand: -7 % 3 is -1. For a remainder in 0..m-1,
    // as for days of the week or positions on a ring, correct it.
    public static int FloorMod(int value, int m) => ((value % m) + m) % m;
    // #endregion
}
