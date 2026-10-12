namespace Assessments.Set2;

// Worked solution. Two pointers, O(n): the timestamps are sorted, so as the window's end moves
// forward, its start only ever moves forward too.
public static class BusiestWindow
{
    public static int MostInWindow(IReadOnlyList<long> timestamps, long windowSeconds)
    {
        var best = 0;
        var start = 0;
        for (var end = 0; end < timestamps.Count; end++)
        {
            while (timestamps[end] - timestamps[start] > windowSeconds)
            {
                start++;
            }
            best = Math.Max(best, end - start + 1);
        }
        return best;
    }
}
