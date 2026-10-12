---
title: How a stage page works
track: shared
status: written
---

# How a stage page works

Every stage in a journey uses the same nine sections, in the same order. Once you know the layout, you can find what you need quickly. More importantly, the order is designed to make you think before you read the answer.

## The nine sections

1. **Where we are.** A sentence or two on what the previous stage fixed, and the new symptom that fix led to. This is the trigger for the stage.
2. **Stop and think.** Two to four questions to answer before reading on. You write your answer first, then compare it with the model answer.
3. **The mental model.** How the thing works, from first principles, with a diagram where the structure matters.
4. **The escalation ladder.** The options, from simplest and cheapest to most complex. Each one says what it fixes, what it costs and what it breaks.
5. **Trade-offs at a glance.** The ladder compressed into one table.
6. **Traps.** Common mistakes, including at least one "spot the bug" exercise.
7. **In the interview.** How the question is usually asked, a short model answer, and follow-up questions to practise on.
8. **Try it.** Something to do: a lab against the sample banking API, a design prompt or a code fix.
9. **What breaks next.** The problem this stage's fix creates, which leads into the next stage.

## What the interactive parts look like

### Stop and think

Write an answer before you look. It doesn't need to be long or right. The point is to commit, so the model answer has something to correct. Your answer stays in this browser, so you can come back to it.

::: think The transactions endpoint has an average response time of 80 ms, yet customers say it feels slow. Why might both be true?

An average hides the slowest requests. If most requests take 40 ms and a few take two seconds, the average still looks healthy while some customers wait. That's why the first stage of [B1](/backend/journeys/your-api-gets-popular/) starts with percentiles (p50, p95, p99) rather than averages.

It's also worth asking *which* customers. Slow requests often cluster: one large account, one region, one time of day.

:::

### Spot the bug

Wrong code only ever appears in a block like this one, labelled as a bug to find. Code anywhere else on the site is meant to be correct and to compile.

::::: trap
This check decides whether an account can cover a payment. It compiles and usually works. What's wrong with it?

```csharp
public static bool CanCover(double balance, double payment) =>
    balance - payment >= 0;

// CanCover(0.3, 0.1 + 0.2) returns false.
```

::: details Answer
`double` is a binary floating-point type, so most decimal amounts can't be stored exactly. `0.1 + 0.2` is slightly more than `0.3`, so an account with exactly enough money is refused. Use `decimal` for money: it stores base-10 values exactly, within its range and precision.
:::
:::::

### In the interview

::::: interview
**Usually asked as:** "How would you investigate a slow endpoint?"

**Short answer:** measure before changing anything. Look at latency percentiles to see who is affected and when, find where the time goes (application, database or network), and only then pick a fix.

::: details Follow-up: what if p99 is slow but p50 is fine?
The typical request is fine, so look at what makes the slow ones different: a larger account, a cold cache, lock contention, a garbage-collection pause, or a slow dependency. Compare the slow requests against the rest.
:::
:::::

## Getting the most out of a stage

- Answer the stop-and-think questions honestly, even when you're unsure. A wrong answer you've written down teaches more than a right one you only skimmed.
- Do the "Try it" lab when one exists. Seeing a query plan change, or a connection pool run dry, sticks far better than reading about it.
- Read "What breaks next" before moving on. Spotting the next problem is the skill the journey is building.
