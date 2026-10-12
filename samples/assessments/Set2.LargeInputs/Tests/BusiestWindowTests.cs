namespace Assessments.Set2;

public class BusiestWindowTests
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example() => Assert.Equal(3, BusiestWindow.MostInWindow([0, 10, 50, 55, 59, 200], 10));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example_window_includes_both_ends() => Assert.Equal(2, BusiestWindow.MostInWindow([100, 160], 60));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_transactions() => Assert.Equal(0, BusiestWindow.MostInWindow([], 60));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Zero_length_window_counts_identical_timestamps() =>
        Assert.Equal(3, BusiestWindow.MostInWindow([5, 7, 7, 7, 9], 0));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Timestamps_beyond_int() =>
        Assert.Equal(2, BusiestWindow.MostInWindow([4_000_000_000_000, 4_000_000_000_030, 4_000_000_000_100], 60));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Random_medium_inputs()
    {
        var random = new Random(7);
        for (var round = 0; round < 50; round++)
        {
            var times = Enumerable.Range(0, random.Next(1, 300)).Select(_ => (long)random.Next(0, 1_000)).Order().ToArray();
            var window = random.Next(0, 100);
            Assert.Equal(BruteForce(times, window), BusiestWindow.MostInWindow(times, window));
        }
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_all_in_one_window()
    {
        var times = Enumerable.Repeat(1_000L, 200_000).ToArray();
        Assert.Equal(200_000, await TimeLimit.Within(TimeSpan.FromSeconds(2), () => BusiestWindow.MostInWindow(times, 60)));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_evenly_spread()
    {
        var times = Enumerable.Range(0, 200_000).Select(i => (long)i).ToArray();
        Assert.Equal(10_000, await TimeLimit.Within(TimeSpan.FromSeconds(2), () => BusiestWindow.MostInWindow(times, 9_999)));
    }

    private static int BruteForce(long[] times, long window) =>
        times.Length == 0 ? 0 : times.Max(start => times.Count(t => t >= start && t - start <= window));
}
