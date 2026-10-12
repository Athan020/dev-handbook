---
title: Coding questions
track: shared
sources: [01-fundamentals/algorithms-data-structures.md]
status: written
---

# Coding questions

The most common question type: a problem statement, a few examples, constraints, and an editor. Your code runs against hidden tests, and each test passes only if your output matches exactly.

This page is about what makes a correct idea score well or badly. For the algorithms themselves, use the resources linked from the [pattern map](#a-short-pattern-map) below.

## Stop and think

::: think Your solution passes both sample tests. Three of ten hidden tests fail with "Wrong answer". What are the likely causes, before you even look at the code?

Usually one of these, roughly in order of how often they happen:

1. **An edge case**: empty input, a single item, a value exactly at a boundary, duplicates, negative numbers.
2. **Overflow**: a total or product that doesn't fit in 32 bits.
3. **The output format**: an extra space, a missing newline, the wrong number of decimal places, items in the wrong order.
4. **A misread statement**: "at most" read as "less than", or a tie-breaking rule missed.

"Wrong answer" on a few tests is almost never a wrong algorithm; that would fail the samples too. Reread the statement and the constraints, then test the cases above with custom input.

:::

## Two ways to receive input

Platforms present coding questions in one of two styles. Practise both before the test.

**A function to complete.** The platform reads the input, calls your function and checks what it returns. You only write the body:

<<< @/../samples/assessments/Set1.Warmup/Problems/Overdraft.cs

Don't print anything that isn't asked for; and don't change the signature, or the platform's code won't compile against it.

**A complete program.** Your code reads standard input and writes standard output, and the output is compared with the expected text. You have to parse the input yourself, which is where time goes if you haven't practised it. This template reads every number regardless of how the lines are broken, and writes output through one buffered writer:

<<< @/../samples/assessments/Toolkit/Stdin/Program.cs#program

The [C# toolkit](../csharp-toolkit) has more on fast input and output.

## Why hidden tests fail

::::: trap
A question asks for the average balance, rounded down, of up to 100,000 accounts whose balances are between 0 and 10⁹. This passes both samples. Which hidden tests will it fail?

```csharp
public static int AverageBalance(int[] balances)
{
    int sum = 0;
    foreach (var balance in balances)
    {
        sum += balance;
    }
    return sum / balances.Length;
}
```

::: details Answer
- **Overflow.** Three balances near 10⁹ already exceed `int`'s maximum of about 2.1 × 10⁹. C# doesn't complain by default: `sum` wraps around to a negative number, and the answer is silently wrong. Sum into a `long`.
- **Empty input.** If the constraints allow zero accounts, `balances.Length` is 0, and integer division by zero throws `DivideByZeroException`. Check what the statement says the answer should be.

Integer division rounding down is correct here only because the statement asked for that, and balances aren't negative. For negative numbers, C# rounds towards zero, not down.
:::
:::::

Beyond edge cases and overflow, hidden tests often catch:

- **Time limits.** "Terminated due to timeout" on the large tests means the complexity is wrong. See [reading constraints](../reading-constraints).
- **Output that's nearly right.** Decimal places (`0.5` vs `0.50`), separators, ordering when there are ties, and debugging lines left in the output. Write debugging output to `Console.Error`.
- **Regional settings.** Parsing or printing decimals with the machine's culture can turn `1.5` into `1,5`. Use `CultureInfo.InvariantCulture`, as the template above does.
- **Changing the input.** Sorting the array you were given changes it for the caller, which can break a function-style question's own checks.
- **Deep recursion.** A recursive solution that's fine for the samples can overflow the stack on a list or tree 100,000 deep. Use an explicit stack for deep inputs.

## A short pattern map

Recognising the shape of a problem is most of the work. This map lists the common signals; learn the patterns themselves from a dedicated resource such as the [NeetCode roadmap](https://neetcode.io/roadmap), [LeetCode's study plans](https://leetcode.com/studyplan/) or the [Tech Interview Handbook](https://www.techinterviewhandbook.org/algorithms/study-cheatsheet/).

| When the problem says or implies | Consider |
|---|---|
| "Have we seen this before?", counting, pairs that sum to a target | A hash map or hash set |
| A sorted array, or "find the first/last that satisfies…" | Binary search, or two pointers |
| A contiguous range: "longest", "at most k", "within 60 seconds" | A sliding window |
| Many questions about sums of ranges | Prefix sums |
| "The k largest", "the next earliest", scheduling | A heap (`PriorityQueue`) |
| Overlapping intervals, meetings, bookings | Sort by start time, then sweep |
| Connections, networks, "can you reach…" | A graph: breadth-first or depth-first search |
| Shortest route with equal-cost steps | Breadth-first search |
| "How many ways", "the minimum cost", choices that build on smaller choices | Dynamic programming |
| "All combinations", n ≤ 20 | Backtracking or bitmasks |
| Matching brackets, "the most recent unmatched" | A stack |

## Practise

[Practice set 1](../practice/#set-1-warm-up) is three function-style problems with the kinds of hidden tests described here. [Practice set 2](../practice/#set-2-large-inputs) adds large inputs.
