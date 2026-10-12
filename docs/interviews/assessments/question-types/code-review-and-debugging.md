---
title: Code review and debugging
track: shared
status: written
---

# Code review and debugging

Some questions hand you code instead of a blank editor. "This function sometimes returns the wrong fee. Find and fix the bug." Or: "Review this change as you would a colleague's pull request." They test whether you can read code you didn't write, which is most of the job.

## Stop and think

::: think You're given a 40-line function and told it has a bug. How do you find it without reading every line equally carefully?

1. **Work out what it's meant to do**, from its name, comments and the question, before reading how it does it.
2. **Find an input that breaks it.** The question often hints: "sometimes", "for large amounts", "at the end of the month". Try the boundaries: empty, one item, the first and last positions, exact limits, ties.
3. **Check the usual suspects** below, in the order the code makes likely.
4. **Fix the bug and nothing else.** Rewriting the function risks new bugs, and the scoring tests were written against the original's behaviour everywhere else.

:::

::: think You're asked to review a pull request. What goes in your comments, and in what order?

Correctness first, then security, then things that will hurt in production (performance, error handling, resource use), then maintainability, then style. A review that lists ten naming nits and misses a race condition has failed.

For each comment, say what the problem is, why it matters (a concrete input or scenario), and what you'd suggest. Mark which comments block the change and which are suggestions.

:::

## The usual suspects

| Category | Look for |
|---|---|
| Boundaries | `<` vs `<=`, loops that start or end one item off, `Length` vs `Length - 1`, empty input |
| Numbers | `int` overflow, integer division, `double` for money, rounding rules |
| NULLs and missing values | Dereferencing something that can be null, `First()` on an empty sequence, a missing dictionary key |
| Mutation and sharing | Changing a collection while iterating it, modifying an input the caller still uses, a shared mutable field |
| Equality | Comparing references instead of values, floating-point `==`, case or culture in string comparisons |
| Resources | Missing `using` on disposables, connections or streams left open on an exception path |
| Async | `.Result` or `.Wait()`, `async void`, a missing `await`, a task started and never observed |
| Concurrency | Check-then-act without a lock or transaction, non-thread-safe collections shared between threads |
| Errors | Exceptions swallowed by an empty `catch`, errors logged and then ignored, retrying something that isn't safe to retry |
| Time and culture | `DateTime.Now` instead of UTC, time zones, parsing and formatting with the machine's regional settings |

## Two to practise

::::: trap
The specification says: "The fee is 0.5% of the amount, rounded to the nearest cent, with halves rounded up." For an amount of 533.00, the expected fee is 2.67. This returns 2.66. Why?

```csharp
public static class Fees
{
    // The fee is 0.5% of the amount, rounded to the nearest cent, with halves rounded up.
    public static double TransferFee(double amount)
    {
        return Math.Round(amount * 0.005, 2);
    }
}
```

::: details Answer
Two bugs, and fixing only one leaves the test failing.

- **`Math.Round` rounds halves to the nearest even digit by default** ("banker's rounding"), so 2.665 becomes 2.66. The specification wants halves rounded up: pass `MidpointRounding.AwayFromZero`.
- **`double` can't represent most decimal fractions exactly.** `Math.Round(1.005, 2)` returns 1, because 1.005 is stored as 1.00499999…, so even with the right rounding mode, some amounts round the wrong way. Use `decimal` for money.

```csharp
public static decimal TransferFee(decimal amount) =>
    Math.Round(amount * 0.005m, 2, MidpointRounding.AwayFromZero);
```
:::
:::::

::::: trap
This should return the index of the first timestamp at or after `time` in a sorted array, or `times.Length` if there isn't one. It passes most tests. Which input breaks it?

```csharp
public static class Search
{
    public static int FirstAtOrAfter(long[] times, long time)
    {
        int low = 0, high = times.Length - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (times[mid] < time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return low;
    }
}
```

::: details Answer
A `time` after every element. For `[10, 20, 30]` and 35, the answer should be 3 ("none"), but `high` starts at the last index, so the search can never return past it, and it returns 2: an index of an element that's before `time`.

The search range has to include the "not found" position. Start with `high = times.Length`. The loop then works unchanged, and handles an empty array too. In real code, `Array.BinarySearch` and `List<T>.BinarySearch` exist, but their return value for a missing item (the bitwise complement of the insertion point) is its own trap.
:::
:::::

## More from the journeys

Every stage of the backend journeys ends with a "spot the bug" exercise taken from realistic code. They make good review practice, because each one passes its tests and fails in production:

| Bug | Where |
|---|---|
| A percentile function that never sorts, and throws at p = 1.0 | [B1 stage 1](/backend/journeys/your-api-gets-popular/01-measure-first#traps) |
| A query that can't use the index it seems to match | [B1 stage 2](/backend/journeys/your-api-gets-popular/02-indexes-and-query-shape#traps) |
| An ORM query that's correct and slow (N+1) | [B1 stage 3](/backend/journeys/your-api-gets-popular/03-what-is-the-orm-doing#traps) |
| Blocking on async code with `.Result` | [B1 stage 4](/backend/journeys/your-api-gets-popular/04-too-many-connections#traps) |
| Clearing the cache before the commit | [B1 stage 5](/backend/journeys/your-api-gets-popular/05-cache-then-invalidate#traps) |
| Reading your own write from a replica | [B1 stage 6](/backend/journeys/your-api-gets-popular/06-read-replicas#traps) |
| A shard router that routes differently after every restart | [B1 stage 7](/backend/journeys/your-api-gets-popular/07-partitioning-and-sharding#traps) |
| Background work that dies with the process | [B2 stage 1](/backend/journeys/work-outside-the-request/01-get-it-out-of-the-request#traps) |
| A consumer whose queue disappears during deployments | [B2 stage 2](/backend/journeys/work-outside-the-request/02-queues-topics-or-streams#traps) |
| A duplicate check that isn't atomic with the work | [B2 stage 3](/backend/journeys/work-outside-the-request/03-delivered-twice#traps) |
| Saving to the database and publishing, not atomically | [B2 stage 4](/backend/journeys/work-outside-the-request/04-dual-write-and-outbox#traps) |
| Deduplication that drops late events | [B2 stage 5](/backend/journeys/work-outside-the-request/05-ordering#traps) |
| A consumer stalled by one bad message | [B2 stage 6](/backend/journeys/work-outside-the-request/06-poison-messages#traps) |
| A batching loop that doesn't compile, then loses messages | [B2 stage 7](/backend/journeys/work-outside-the-request/07-backpressure#traps) |

Try each one before reading the stage it comes from: the bug is findable without the context.
