namespace Assessments.Set2;

public class TellersTests
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example() =>
        Assert.Equal(2, Tellers.Minimum([new(0, 30), new(10, 20), new(25, 40), new(35, 50)]));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example_a_teller_freed_at_the_arrival_time_can_serve() =>
        Assert.Equal(1, Tellers.Minimum([new(0, 10), new(10, 20)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_customers() => Assert.Equal(0, Tellers.Minimum([]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Visits_not_in_time_order() =>
        Assert.Equal(3, Tellers.Minimum([new(50, 60), new(0, 100), new(55, 70), new(10, 20)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Random_medium_inputs()
    {
        var random = new Random(13);
        for (var round = 0; round < 50; round++)
        {
            var visits = Enumerable.Range(0, random.Next(1, 200)).Select(_ =>
            {
                var start = random.Next(0, 1_000);
                return new Visit(start, start + random.Next(1, 100));
            }).ToArray();
            Assert.Equal(BruteForce(visits), Tellers.Minimum(visits));
        }
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_back_to_back()
    {
        var visits = Enumerable.Range(0, 200_000).Select(i => new Visit(i, i + 1)).Reverse().ToArray();
        Assert.Equal(1, await TimeLimit.Within(TimeSpan.FromSeconds(2), () => Tellers.Minimum(visits)));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_everyone_at_once()
    {
        var visits = Enumerable.Repeat(new Visit(0, 10), 200_000).ToArray();
        Assert.Equal(200_000, await TimeLimit.Within(TimeSpan.FromSeconds(2), () => Tellers.Minimum(visits)));
    }

    // The most customers being served at any moment: check every start time.
    private static int BruteForce(Visit[] visits) =>
        visits.Max(at => visits.Count(v => v.Start <= at.Start && at.Start < v.End));
}
