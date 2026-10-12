---
title: Reading constraints
track: shared
sources: [01-fundamentals/algorithms-data-structures.md]
status: written
---

# Reading constraints

A question asks for the number of pairs of transactions whose amounts add up to a target. You write two nested loops, it passes both sample tests, and you submit. Six hidden tests pass; four fail with "Terminated due to timeout". The constraints said *1 ≤ n ≤ 200,000*, and you didn't read them.

The constraints section is the most useful part of a question you'll skip. It tells you, before you write any code, how fast your solution must be, which data types you need, and which edge cases the hidden tests contain.

## Stop and think

::: think n is at most 200,000. Why does the nested-loop solution time out, when it was instant on the samples?

The samples had five items: 10 pairs. With 200,000 items there are about 2 × 10¹⁰ pairs. On a laptop, a tight C# loop checked about 6 × 10⁸ pairs a second, so the nested loops would take over 30 seconds. With a hash map counting the amounts seen so far, the same input took 7 milliseconds.

Time limits are usually a few seconds at most (each platform documents its own), so the question was really saying: "O(n²) won't do. Find something around O(n log n) or better."

:::

::: think The amounts are integers up to 10⁹, and there are up to 200,000 of them. You need their total. What type do you use?

A 64-bit integer (`long` in C#). The total can reach 2 × 10¹⁴, far beyond a 32-bit `int`, whose maximum is about 2.1 × 10⁹. Even two amounts added together can overflow an `int`. In C#, the overflow is silent unless you ask for checking, so the answer is just wrong, with no error. Hidden tests with large values exist to catch this.

:::

## From input size to target complexity

A rough rule: assume about 10⁸ simple operations a second, which leaves margin for slower machines, heavier operations and constant factors. Then the input size tells you which complexities fit:

| Largest n | Complexity that fits | Typical approaches |
|---|---|---|
| about 10 | O(n!) | Try every ordering |
| about 20 | O(2ⁿ) | Try every subset, bitmasks |
| a few hundred | O(n³) | Triple loops, some dynamic programming |
| a few thousand | O(n²) | Nested loops, dynamic programming over pairs |
| 10⁵ to 10⁶ | O(n log n) or O(n) | Sorting, hash maps, two pointers, heaps, prefix sums, binary search |
| 10⁹ and beyond | O(log n) or O(1) | Maths, or binary search on the answer |

This is a guide to what the question setter intended, not a law. A smaller n than you'd expect is often a hint that a simpler, slower approach is acceptable.

## What else the constraints tell you

- **The value range decides your types.** Values up to 10⁹ mean sums and products need 64 bits. "Return the answer modulo 10⁹ + 7" means the true answer is huge, and you must take the remainder as you go, not at the end.
- **Memory is a constraint too.** A million `int`s is 4 MB, which is fine. A table of n × n with n = 100,000 is 10¹⁰ cells, which isn't.
- **Several test cases in one input.** "The sum of n over all test cases is at most 2 × 10⁵" means the total work is what counts, so don't reset large arrays for every test case.
- **The edge cases are listed for you.** "0 ≤ n" means an empty input is a test. "Values may be negative" means negatives are a test. "Strings contain lower-case letters and spaces" means spaces are a test.
- **Guarantees are permissions.** "The input is sorted by time" or "all ids are unique" mean you don't need to handle the alternative. Don't spend time on it.

## Try it

For each, decide the target complexity and an approach before opening the answer.

::: think n ≤ 200,000 daily totals and q ≤ 200,000 questions, each asking for the sum of the totals between day a and day b.

Answering each question with a loop is O(n × q), up to 4 × 10¹⁰: too slow. Compute prefix sums once in O(n), where `prefix[i]` is the sum of the first i days; then each question is `prefix[b + 1] - prefix[a]`, in O(1). Total O(n + q). And the sums need 64 bits. This is problem 2 of [practice set 2](./practice/#set-2-large-inputs).

:::

::: think n ≤ 20 standing orders. Is there a subset whose amounts add up exactly to a target?

n is tiny, which is the hint: 2²⁰ is about a million subsets, so trying all of them is fine. With n up to 10⁵, you'd need a different approach (and the problem would usually limit the amounts so that dynamic programming over amounts fits).

:::

::: think A list of up to 100,000 sorted timestamps. For each, how many timestamps fall in the 60 seconds before it?

O(n²) is 10¹⁰: too slow. Because the list is sorted, use two pointers: as the right end moves forward, move the left end forward until it's within 60 seconds. Each pointer only ever moves forward, so it's O(n) overall. Binary search for the left end of each window works too, in O(n log n).

:::

::: think A balance is multiplied by the same whole-number factor every day, for n ≤ 10¹⁸ days. Return the final balance modulo 10⁹ + 7.

Even O(n) is far too slow: 10¹⁸ steps would take decades. The size is the signal that you need O(log n). Raising to the power n by repeated squaring (`x²`, `x⁴`, `x⁸`, and so on) takes at most about 120 multiplications for n = 10¹⁸: one squaring per binary digit of n, plus one multiplication for each 1 digit. Take the remainder after every multiplication, and multiply in 64 bits: two numbers below 10⁹ + 7 multiply to less than 2⁶³, but not less than 2³¹.

:::

## "Terminated due to timeout"

When some hidden tests pass and others time out, the logic is usually right and the complexity is wrong. The tests that time out are the large ones. Don't look for a bug; look for a better algorithm, starting from the table above. HackerRank's note on the [timeout status](https://candidatesupport.hackerrank.com/articles/4495712051-terminated-due-to-timeout-status) says the same, and the [execution environment](https://candidatesupport.hackerrank.com/articles/2201684846-execution-environment) lists its time limits for each language.

[Practice set 2](./practice/#set-2-large-inputs) is built around exactly this: each problem has a simple solution that passes the small tests and times out on the large ones.
