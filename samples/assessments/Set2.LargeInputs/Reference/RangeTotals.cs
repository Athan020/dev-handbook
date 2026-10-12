namespace Assessments.Set2;

// Worked solution. Prefix sums, O(n + q): prefix[i] is the total of the first i days, so any
// range total is the difference of two prefix sums. Totals need 64 bits.
public static class RangeTotals
{
    public static long[] Answer(IReadOnlyList<long> dailyTotals, IReadOnlyList<DayRange> questions)
    {
        var prefix = new long[dailyTotals.Count + 1];
        for (var i = 0; i < dailyTotals.Count; i++)
        {
            prefix[i + 1] = prefix[i] + dailyTotals[i];
        }

        var answers = new long[questions.Count];
        for (var i = 0; i < questions.Count; i++)
        {
            answers[i] = prefix[questions[i].To + 1] - prefix[questions[i].From];
        }
        return answers;
    }
}
