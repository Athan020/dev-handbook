namespace Assessments.Set2;

// Worked solution. Sort by start time, and keep a min-heap of the times at which busy tellers
// become free. O(n log n). A teller who becomes free at the moment a customer arrives can serve them.
public static class Tellers
{
    public static int Minimum(IReadOnlyList<Visit> visits)
    {
        var busyUntil = new PriorityQueue<long, long>();
        var most = 0;
        foreach (var visit in visits.OrderBy(v => v.Start))
        {
            while (busyUntil.Count > 0 && busyUntil.Peek() <= visit.Start)
            {
                busyUntil.Dequeue();
            }
            busyUntil.Enqueue(visit.End, visit.End);
            most = Math.Max(most, busyUntil.Count);
        }
        return most;
    }
}
