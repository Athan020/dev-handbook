namespace Assessments.Set2;

public class RangeTotalsTests
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public void Example() =>
        Assert.Equal([60L, 10, 15], RangeTotals.Answer([10, 20, 30, -35], [new(0, 2), new(0, 0), new(1, 3)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void No_questions() => Assert.Empty(RangeTotals.Answer([1, 2, 3], []));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Single_day() => Assert.Equal([-7L], RangeTotals.Answer([-7], [new(0, 0)]));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public void Random_medium_inputs()
    {
        var random = new Random(11);
        var days = Enumerable.Range(0, 500).Select(_ => (long)random.Next(-1_000_000_000, 1_000_000_000)).ToArray();
        var questions = Enumerable.Range(0, 500).Select(_ =>
        {
            int a = random.Next(days.Length), b = random.Next(days.Length);
            return new DayRange(Math.Min(a, b), Math.Max(a, b));
        }).ToArray();
        var expected = questions.Select(q => days[q.From..(q.To + 1)].Sum()).ToArray();
        Assert.Equal(expected, RangeTotals.Answer(days, questions));
    }

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Large_input_beyond_int()
    {
        var days = Enumerable.Repeat(1_000_000_000L, 200_000).ToArray();
        var questions = Enumerable.Repeat(new DayRange(0, 199_999), 200_000).ToArray();
        var answers = await TimeLimit.Within(TimeSpan.FromSeconds(2), () => RangeTotals.Answer(days, questions));
        Assert.All(answers, a => Assert.Equal(200_000_000_000_000L, a));
    }
}
