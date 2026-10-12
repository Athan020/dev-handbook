namespace Assessments.Toolkit;

public class ToolkitTests
{
    [Fact]
    public void Equal_priorities_keep_their_arrival_order()
    {
        var payments = Enumerable.Range(0, 50).Select(i => new Payment($"p{i}", i % 2)).ToList();
        var expected = payments.Where(p => p.Priority == 0).Concat(payments.Where(p => p.Priority == 1)).Select(p => p.Id);
        Assert.Equal(expected, Heaps.ProcessingOrder(payments));
    }

    [Fact]
    public void A_plain_priority_queue_is_not_stable()
    {
        // Why the tiebreaker is needed: without it, equal priorities come out in no particular order.
        var queue = new PriorityQueue<int, int>();
        for (var i = 0; i < 50; i++)
        {
            queue.Enqueue(i, 0);
        }
        var order = new List<int>();
        while (queue.TryDequeue(out var item, out _))
        {
            order.Add(item);
        }
        Assert.NotEqual(Enumerable.Range(0, 50), order);
    }

    [Fact]
    public void Top_k() => Assert.Equal([90L, 70, 50], Heaps.Largest([10, 90, 50, 30, 70, 20], 3));

    [Fact]
    public void Max_heap_dequeues_the_largest_first()
    {
        var queue = Heaps.LargestFirst();
        queue.Enqueue("small", 10);
        queue.Enqueue("large", 500);
        queue.Enqueue("medium", 100);
        Assert.Equal("large", queue.Dequeue());
    }

    [Fact]
    public void Sorted_set_ranges()
    {
        var timestamps = new SortedSet<long> { 10, 20, 30, 40 };
        Assert.Equal(2, Ordered.CountBetween(timestamps, 15, 30));
        Assert.Equal(30, Ordered.NextAtOrAfter(timestamps, 25));
        Assert.Null(Ordered.NextAtOrAfter(timestamps, 41));
    }

    [Fact]
    public void Multiset_keeps_duplicates()
    {
        var counts = new SortedDictionary<long, int>();
        Ordered.Add(counts, 5);
        Ordered.Add(counts, 5);
        Ordered.Remove(counts, 5);
        Assert.Equal(1, counts[5]);
        Ordered.Remove(counts, 5);
        Assert.Empty(counts);
    }

    [Fact]
    public void Sorting_by_several_keys()
    {
        var accounts = new List<Account> { new(3, "Zola", 100), new(1, "Anna", 100), new(2, "Anna", 100), new(4, "Ben", 900) };
        Sorting.SortForReport(accounts);
        Assert.Equal([4, 1, 2, 3], accounts.Select(a => a.Id));
    }

    [Fact]
    public void OrderBy_is_stable()
    {
        var accounts = Enumerable.Range(0, 100).Select(i => new Account(i, "x", i % 3)).ToList();
        var sorted = Sorting.ByBalanceKeepingOrder(accounts);
        foreach (var group in sorted.GroupBy(a => a.Balance))
        {
            Assert.Equal(group.Select(a => a.Id).Order(), group.Select(a => a.Id));
        }
    }

    [Fact]
    public void Checked_arithmetic_throws_on_overflow() =>
        Assert.Throws<OverflowException>(() => Numbers.TotalChecked([long.MaxValue, 1]));

    [Fact]
    public void Unchecked_arithmetic_wraps_silently()
    {
        var balance = int.MaxValue;
        balance += 1;
        Assert.Equal(int.MinValue, balance);
    }

    [Fact]
    public void Mod_pow() => Assert.Equal(1, Numbers.ModPow(2, Numbers.Modulus - 1)); // Fermat's little theorem

    [Theory]
    [InlineData(-7, 3, 2)]
    [InlineData(7, 3, 1)]
    [InlineData(-3, 3, 0)]
    public void Floor_mod(int value, int m, int expected) => Assert.Equal(expected, Numbers.FloorMod(value, m));

    [Fact]
    public void Iterative_search_handles_a_very_long_chain()
    {
        const int n = 1_000_000;
        var links = Enumerable.Range(0, n).Select(i => i + 1 < n ? new List<int> { i + 1 } : []).ToList();
        Assert.Equal(n, Graphs.CountReachable(links, 0));
    }
}
