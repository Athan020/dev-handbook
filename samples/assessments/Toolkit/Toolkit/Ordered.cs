namespace Assessments.Toolkit;

public static class Ordered
{
    // #region sorted-set
    // A SortedSet keeps items ordered as they're added and removed, with O(log n) operations.
    // GetViewBetween gives the items in a range without copying them.
    public static int CountBetween(SortedSet<long> timestamps, long from, long to) =>
        timestamps.GetViewBetween(from, to).Count;

    // The nearest timestamp at or after `time`, or null if there's none.
    public static long? NextAtOrAfter(SortedSet<long> timestamps, long time)
    {
        var view = timestamps.GetViewBetween(time, long.MaxValue);
        return view.Count == 0 ? null : view.Min;
    }
    // #endregion

    // #region multiset
    // SortedSet ignores duplicates. To keep repeated values, count them in a SortedDictionary.
    public static void Add(SortedDictionary<long, int> counts, long value) =>
        counts[value] = counts.GetValueOrDefault(value) + 1;

    public static void Remove(SortedDictionary<long, int> counts, long value)
    {
        if (--counts[value] == 0)
        {
            counts.Remove(value);
        }
    }
    // #endregion
}
