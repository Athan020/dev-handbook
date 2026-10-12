namespace Assessments.Set1;

// Worked solution. O(n) time, O(1) extra space.
public static class Overdraft
{
    public static int FirstBreach(long openingBalance, long overdraftLimit, IReadOnlyList<long> amounts)
    {
        var balance = openingBalance; // long: totals can pass int's range long before any one amount does
        for (var i = 0; i < amounts.Count; i++)
        {
            balance += amounts[i];
            if (balance < -overdraftLimit)
            {
                return i;
            }
        }
        return -1;
    }
}
