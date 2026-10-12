---
title: Test-day strategy
track: shared
status: written
---

# Test-day strategy

Ninety minutes, three questions worth 100, 200 and 300 points. A candidate solves the first in fifteen minutes, then spends an hour on the second, chasing the last two hidden tests. With fifteen minutes left, they open the third for the first time. Their final score: 100, plus 160 of 200 for the second question, plus nothing for the third. 260 of 600.

A simple, correct, slow solution to the third question would probably have passed its small tests, and been worth more than the last 40 points of the second.

## Stop and think

::: think You open the test. What do you do in the first five minutes?

Read every question before writing any code: the statement, the constraints and the points. Note for each one roughly how hard it looks, what approach comes to mind, and how much it's worth. Then start with the question you're surest of, not necessarily the first.

Reading all of them first costs a few minutes and buys you a plan. It also lets the harder questions sit at the back of your mind while you work on the easy one.

:::

::: think Fifteen minutes left. Question 2 passes 7 of 10 tests with a slow solution. Question 3, worth 300 points, is untouched. Where do the fifteen minutes go?

Usually to question 3. Making question 2 fast enough means a new algorithm, which may not work in fifteen minutes and is worth at most the last three tests. A brute-force solution to question 3 is often quick to write and passes its small tests, which can be a third or more of its points.

The exception: if question 2's failures look like a small bug, such as an overflow or an off-by-one, fixing that is the cheapest points on the table.

:::

## Spend time where the points are

Give each question time roughly in proportion to its points, and decide your checkpoints before you start. For the example above:

| | Points | Time budget | Checkpoint |
|---|---|---|---|
| Read all questions | | 5 min | 0:05 |
| Question 1 | 100 | 12 min | 0:17 |
| Question 2 | 200 | 25 min | 0:42 |
| Question 3 | 300 | 38 min | 1:20 |
| Review and fix | | 10 min | 1:30 |

When you reach a checkpoint, decide deliberately whether to carry on or move on. If you're close and know exactly what's left, carry on briefly. If you've made no progress for ten or fifteen minutes, move on and come back.

## Bank partial credit, then improve

Points come from tests, and tests come in two kinds: small ones that check correctness and edge cases, and large ones that check speed. A correct brute-force solution earns the first kind. So:

1. **Write the simplest correct solution first**, even if it's too slow for the largest inputs.
2. **Run it, and submit it** if the platform scores your latest submission. Now those points are banked.
3. **Then improve it**, keeping the working version until the new one passes at least as much.

This also protects you from the worst outcome: a clever solution that doesn't quite work, and scores less than the simple one would have.

## Test what the samples don't

Sample tests show the happy path. Hidden tests are where the edge cases live. Before submitting, spend two minutes listing inputs the statement allows but the samples don't show, and run the ones you can with custom input:

- **Empty or minimal input:** no transactions, one account, an empty string.
- **Boundaries:** values exactly at a limit ("at most 60 seconds": test 60 and 61).
- **Duplicates and ties:** equal amounts, equal timestamps, several right answers.
- **Extremes:** the largest values and sizes the constraints allow. Do totals overflow a 32-bit integer?
- **Unusual but valid formats:** lower case, spaces, negative numbers, if the statement allows them.

[Reading constraints](./reading-constraints) shows how to get most of these from the constraints section.

## Don't lose points to the platform

- **Run or submit regularly.** On some platforms, code you typed but never ran or submitted isn't evaluated when time runs out. See HackerRank's note on [evaluation at time-out](https://candidatesupport.hackerrank.com/articles/3576626591-evaluation-of-a-question-during-test-time-out), and check your platform's rules.
- **Never leave code that doesn't compile.** A question that doesn't compile scores nothing, even if most of it was right. In the last ten minutes, comment out the half-finished improvement and leave the working version.
- **Keep standard output clean.** When a question compares your printed output with the expected output, a leftover debugging line makes every test fail. Write debugging output to standard error (`Console.Error.WriteLine`) instead.
- **Read the output format twice.** Separators, decimal places, ordering and trailing spaces all count when outputs are compared exactly.

## When you're stuck

- **Work an example by hand**, on paper, slowly. The pattern you need often shows up in what you do by hand.
- **Shrink the problem**: solve it for two items, then three. Or drop a constraint, solve that, and add it back.
- **Check the constraints** for a hint about the expected approach. See [reading constraints](./reading-constraints).
- **Move on** if none of that helps within your budget. Coming back later with fresh eyes works more often than pushing on.
