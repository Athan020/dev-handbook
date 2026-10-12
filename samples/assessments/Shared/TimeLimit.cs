namespace Assessments;

// Assessment platforms give each test a time limit. A solution with the wrong complexity
// doesn't fail with a wrong answer: it runs out of time. This helper does the same.
public static class TimeLimit
{
    public static async Task<T> Within<T>(TimeSpan limit, Func<T> solve)
    {
        var work = Task.Run(solve);
        try
        {
            return await work.WaitAsync(limit, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Took longer than {limit.TotalSeconds:0.#} s. For inputs this size, the solution probably needs a better time complexity.");
            throw;
        }
    }
}
