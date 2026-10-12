---
title: C# toolkit
track: shared
sources: [01-fundamentals/algorithms-data-structures.md]
status: written
---

# C# toolkit

The parts of C# and .NET that assessments lean on, and the ways they catch people out. This isn't a C# tutorial: it assumes you write C# already, and covers what matters under a time limit with hidden tests.

Every snippet here compiles and has a test in `samples/assessments/Toolkit/`.

## Stop and think

::: think Inside a loop over 100,000 payments, you check whether each payment's id is in a `List<string>` of ids already seen. What happens on the large hidden tests?

`List<T>.Contains` checks every item, so each lookup is O(n) and the loop is O(n²): about 5 × 10⁹ comparisons, and a timeout. A `HashSet<string>` makes each lookup O(1) on average, and the loop O(n). Choosing the right collection is most of what "optimising" means in an assessment.

:::

::: think Your recursive depth-first search works on every visible test. On a hidden test, the process dies with "Stack overflow", and no exception you can catch. Why?

Each recursive call uses some of the thread's stack, and the stack is small: deep enough for a balanced tree, not for a chain of 100,000 linked accounts. In .NET, a stack overflow kills the process outright. When the input can be a long chain, use an explicit `Stack<T>` instead of recursion: see [recursion](#recursion) below.

:::

## Input and output

The [coding page](./question-types/coding#two-ways-to-receive-input) has a complete standard-input template that reads every number regardless of line breaks. Three details matter when you write your own:

- `Console.ReadLine()` returns `null` at the end of the input, not an empty string.
- `line.Split(' ')` produces empty strings wherever there are two spaces in a row. Pass `StringSplitOptions.RemoveEmptyEntries`.
- Parse and print with `CultureInfo.InvariantCulture`, or set `CultureInfo.CurrentCulture` to it once. Otherwise a machine with different regional settings reads and writes `1.5` as `1,5`.

For large outputs, build the text in a `StringBuilder` or write through one buffered `StreamWriter`, as the template does, rather than calling `Console.WriteLine` for every line.

## Numbers

| Type | Largest value | Use for |
|---|---|---|
| `int` | about 2.1 × 10⁹ | Counts and indexes |
| `long` | about 9.2 × 10¹⁸ | Totals, products, timestamps; when in doubt |
| `decimal` | 28 to 29 significant digits | Money: exact decimal fractions |
| `double` | about 15 to 17 significant digits | Measurements, averages; never money |

Overflow is silent by default: `int.MaxValue + 1` is `int.MinValue`. While testing, a `checked` block turns a wrong answer into an exception you can see:

<<< @/../samples/assessments/Toolkit/Toolkit/Numbers.cs#checked

Two more rules that cost points:

- **Integer division rounds towards zero**, and `%` takes the sign of the left operand: `-7 / 2` is -3, and `-7 % 3` is -1. For a remainder that's always between 0 and m − 1, correct it:

  <<< @/../samples/assessments/Toolkit/Toolkit/Numbers.cs#floor-mod

- **"Modulo 10⁹ + 7" answers** need the remainder after every step, and products in `long`. Raising to a large power takes O(log n) multiplications by repeated squaring:

  <<< @/../samples/assessments/Toolkit/Toolkit/Numbers.cs#mod-pow

## Choosing a collection

| Collection | Fast | Slow | Notes |
|---|---|---|---|
| `List<T>` | Index, add at the end | `Contains`, insert or remove at the start: O(n) | The default |
| `HashSet<T>`, `Dictionary<K,V>` | Add, remove, look up: O(1) on average | Nothing ordered | "Have I seen this?", counting |
| `SortedSet<T>`, `SortedDictionary<K,V>` | Add, remove, look up, min, max: O(log n) | | Ordered; ranges; `SortedSet` drops duplicates |
| `PriorityQueue<T,P>` | Add, remove the smallest: O(log n); peek: O(1) | Finding or updating an item: O(n) | Not stable; no "change priority" |
| `Queue<T>`, `Stack<T>` | Both ends: O(1) | Anything in the middle | Breadth-first and depth-first search |

For counting, `counts[key] = counts.GetValueOrDefault(key) + 1;` is the shortest correct form.

## Heaps

`PriorityQueue<TElement, TPriority>` always dequeues the smallest priority. Two things catch people out.

**It isn't stable.** Items with equal priorities come out in no particular order, not the order they went in. If the question says "ties in order of arrival", put the arrival order into the priority:

<<< @/../samples/assessments/Toolkit/Toolkit/Heaps.cs#priority-queue

**It has no "largest first" mode.** Pass a reversed comparer:

<<< @/../samples/assessments/Toolkit/Toolkit/Heaps.cs#max-heap

For "the k largest", keep a min-heap of size k rather than sorting everything. It's O(n log k), and it works on a stream you can only read once:

<<< @/../samples/assessments/Toolkit/Toolkit/Heaps.cs#top-k

There's no way to change an item's priority. Algorithms that need it, such as Dijkstra's shortest paths, enqueue the item again with the new priority, and skip stale entries when they're dequeued.

## Ordered sets

`SortedSet<T>` answers "the next one after this" and "how many in this range" in O(log n):

<<< @/../samples/assessments/Toolkit/Toolkit/Ordered.cs#sorted-set

It silently ignores duplicates. To keep repeated values, count them instead:

<<< @/../samples/assessments/Toolkit/Toolkit/Ordered.cs#multiset

## Sorting

Sort by several keys with tuples, which compare item by item. Swapping `a` and `b` in one position reverses that key:

<<< @/../samples/assessments/Toolkit/Toolkit/Sorting.cs#sort-keys

`List.Sort` and `Array.Sort` aren't stable. When equal items must keep their original order, use LINQ, which is:

<<< @/../samples/assessments/Toolkit/Toolkit/Sorting.cs#stable-sort

::::: trap
A comparison written for brevity. It works on the samples. Which hidden test breaks it?

```csharp
public static void SortAmounts(List<int> amounts) =>
    amounts.Sort((a, b) => a - b);
```

::: details Answer
Large values of opposite sign. For `a = 2,000,000,000` and `b = -2,000,000,000`, `a - b` overflows to a negative number, so the comparer says `a` is smaller, and the sort order is wrong, with no error. Use `a.CompareTo(b)`, or here simply `amounts.Sort()`.
:::
:::::

## Recursion

A recursive solution is often the clearest, and fine when the depth is small, as in a balanced tree or a search over 20 choices. When the depth can reach the input size, as in a chain of linked accounts, a long path, or a degenerate tree, use an explicit stack:

<<< @/../samples/assessments/Toolkit/Toolkit/Graphs.cs#iterative-dfs

The test for this runs a chain of a million accounts. The recursive equivalent crashes long before that.

## LINQ

LINQ is quick to write, and its costs are easy to miss.

::::: trap
This finds accounts whose transactions total more than a limit. It's correct, and passes the visible tests. What happens on a hidden test with 100,000 accounts?

```csharp
public sealed record Transaction(int AccountId, decimal Amount);

public static class Limits
{
    public static List<int> AccountsOverLimit(IEnumerable<Transaction> transactions, decimal limit)
    {
        var result = new List<int>();
        foreach (var accountId in transactions.Select(t => t.AccountId).Distinct())
        {
            var total = transactions.Where(t => t.AccountId == accountId).Sum(t => t.Amount);
            if (total > limit)
            {
                result.Add(accountId);
            }
        }
        return result;
    }
}
```

::: details Answer
It times out. For every account, `Where` scans all the transactions again: with 100,000 accounts and a few hundred thousand transactions, that's tens of billions of checks. And if `transactions` is a sequence that can only be read once, such as lines streamed from a file, the second pass sees nothing at all.

Group once, in one pass:

```csharp
return transactions
    .GroupBy(t => t.AccountId)
    .Where(g => g.Sum(t => t.Amount) > limit)
    .Select(g => g.Key)
    .ToList();
```
:::
:::::

Other LINQ costs to keep in mind:

- **Deferred execution.** A query runs each time it's enumerated, not when it's written, and sees the data as it is then. Call `ToList()` once if you'll use the results more than once.
- **`Count() > 0`** enumerates everything on a plain sequence; `Any()` stops at the first item.
- **`OrderBy(...).First()`** sorts everything, O(n log n), to find one item. `MinBy` and `MaxBy` are O(n).
- **`First()`** throws on an empty sequence. If empty is possible, use `FirstOrDefault()` and handle the default.
