---
title: Multiple-choice questions
track: shared
status: written
---

# Multiple-choice questions

Some assessments add a block of multiple-choice questions: what a snippet prints, the complexity of an operation, which SQL query returns a given result, which HTTP status code fits. They're quick to answer and quick to get wrong, usually by reading too fast rather than by not knowing.

## Stop and think

::: think Twenty multiple-choice questions in 20 minutes. Should you answer every one, even when you're guessing?

Check the rules first. If wrong answers cost nothing, answer everything: a guess between two remaining options is a 50% chance, a blank is zero. If wrong answers are penalised (negative marking), guess only when you can eliminate enough options that the expected score is positive. The invitation or the instructions screen usually says which applies; if it doesn't, assume no penalty.

:::

## How to answer them

- **Read the whole question twice**, especially words like *not*, *always*, *never*, *best* and *first*. "Which of these does *not* compile?" is easy to answer backwards.
- **Answer before you read the options**, if you can. Then find your answer among them. Reading the options first makes plausible wrong answers more persuasive.
- **Eliminate.** Two options are usually clearly wrong. Choosing between the remaining two is a better bet than choosing among four.
- **Trace code on paper.** For "what does this print?", write the variables down and update them line by line. Don't run it in your head at reading speed.
- **"Select all that apply"** may score each option separately or the whole question at once; the instructions usually say. Judge each option on its own.
- **Don't get stuck.** If the platform lets you flag questions and come back, use it. Each question is usually worth the same, so a hard one deserves no more time than an easy one.

## What they usually test

For backend roles, expect questions on:

- **Language behaviour**: value and reference types, integer division, NULL handling, deferred execution, exceptions, `async` and `await`. The [C# toolkit](../csharp-toolkit) covers the parts that trip people up.
- **Complexity**: of common operations on lists, dictionaries, sets and sorted collections, and of short snippets.
- **SQL**: which query returns a given result, what a join produces, how NULLs behave. See [SQL questions](./sql).
- **Web and systems fundamentals**: HTTP methods and status codes, caching, idempotency, transactions. The [backend core self-check](/backend/core-self-check) covers the basics.

## Try these

Answer each before opening it.

```csharp
int[] balances = [1, 2, 3];
var large = balances.Where(b => b > 1);
balances[0] = 5;
Console.WriteLine(large.Count());
```

::: think What does the snippet above, with `Where`, print?

3. `Where` doesn't run when it's called; it runs each time the result is enumerated. By the time `Count()` runs, the first balance is 5, so three balances are greater than 1. This is deferred execution, and it's behind several classic bugs.

:::

::: think What does `Console.WriteLine(7 / 2 * 2.0);` print?

6. `7 / 2` is integer division, giving 3, before the multiplication converts anything to `double`. Evaluation is left to right for operators of equal precedence.

:::

```csharp
var points = new List<Point> { new(1, 1) };
var p = points[0];
p.X = 10;
Console.WriteLine(points[0].X);
```

::: think `Point` is a `struct` with a public field `X`. What does the snippet above print?

1. A struct is a value type, so `points[0]` returns a copy, and changing the copy doesn't change the list. With a `class`, it would print 10.

:::

::: think What's the time complexity of inserting an item at the start of a `List<T>` holding n items?

O(n). A `List<T>` is backed by an array, so every existing item has to move up one place. Adding at the end is O(1) on average. If you need cheap inserts at both ends, a `LinkedList<T>` or a double-ended queue is the better structure.

:::

::: think What does `Console.WriteLine(0.1 + 0.2 == 0.3);` print?

`False`. Neither 0.1 nor 0.2 can be stored exactly as a `double`, and their sum is 0.30000000000000004. Compare floating-point numbers within a tolerance, and use `decimal` for money.

:::
