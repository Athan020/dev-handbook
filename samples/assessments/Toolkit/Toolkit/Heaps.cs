namespace Assessments.Toolkit;

public sealed record Payment(string Id, int Priority);

public static class Heaps
{
    // #region priority-queue
    // Process urgent payments first (lower number = more urgent), and payments of equal
    // priority in the order they arrived. PriorityQueue isn't stable on its own, so the
    // arrival order goes into the priority as a tiebreaker.
    public static List<string> ProcessingOrder(IEnumerable<Payment> payments)
    {
        var queue = new PriorityQueue<Payment, (int Priority, int Arrival)>();
        var arrival = 0;
        foreach (var payment in payments)
        {
            queue.Enqueue(payment, (payment.Priority, arrival++));
        }

        var order = new List<string>();
        while (queue.TryDequeue(out var payment, out _))
        {
            order.Add(payment.Id);
        }
        return order;
    }
    // #endregion

    // #region top-k
    // The k largest amounts, in O(n log k): keep a min-heap of the best k seen so far, and
    // drop its smallest whenever it grows past k.
    public static long[] Largest(IEnumerable<long> amounts, int k)
    {
        var best = new PriorityQueue<long, long>();
        foreach (var amount in amounts)
        {
            best.Enqueue(amount, amount);
            if (best.Count > k)
            {
                best.Dequeue();
            }
        }
        return best.UnorderedItems.Select(item => item.Element).OrderDescending().ToArray();
    }
    // #endregion

    // #region max-heap
    // PriorityQueue dequeues the smallest priority first. For largest first, reverse the comparer.
    public static PriorityQueue<string, long> LargestFirst() =>
        new(Comparer<long>.Create((a, b) => b.CompareTo(a)));
    // #endregion
}
