namespace Assessments.Toolkit;

public static class Graphs
{
    // #region iterative-dfs
    // Depth-first search with an explicit stack instead of recursion. A recursive version
    // overflows the call stack on long chains, such as 100,000 accounts each linked to the next.
    public static int CountReachable(IReadOnlyList<List<int>> links, int start)
    {
        var seen = new bool[links.Count];
        var stack = new Stack<int>();
        stack.Push(start);
        seen[start] = true;
        var count = 0;

        while (stack.TryPop(out var node))
        {
            count++;
            foreach (var next in links[node])
            {
                if (!seen[next])
                {
                    seen[next] = true;
                    stack.Push(next);
                }
            }
        }
        return count;
    }
    // #endregion
}
