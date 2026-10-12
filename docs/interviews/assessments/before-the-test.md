---
title: Before the test
track: shared
status: written
---

# Before the test

A candidate practises for weeks on algorithm problems, in their own editor, with autocompletion. On the day, the assessment is in a browser editor without autocompletion. The first question reads its input from standard input instead of handing it to a function. Ten minutes go on parsing input, and another five on finding out why `Console.ReadLine()` returned `null`. None of it was about the problem.

Most of what goes wrong on the day can be found and fixed in an hour beforehand.

## Stop and think

::: think You have an invitation and a week. What would you do before starting the real test?

- **Take the platform's sample or practice test**, in the same browser and on the same machine you'll use. It's the only way to meet its editor, its run and submit buttons, and its input format before it costs you time.
- **Check your language.** Is it allowed? Which version does the platform run? Code that uses a newer language feature than the platform supports won't compile there.
- **Prepare for both input styles**: a function you complete, and a program that reads standard input and writes standard output. Know how to read a line, split it and parse numbers without looking it up.
- **Check your setup**: browser, webcam and microphone if it's proctored, a stable connection, power, and a quiet room with notifications off.
- **Do one timed practice set** under realistic conditions, to find out how you use time when the clock is running.

:::

::: think You're more comfortable in your own editor. Should you write your code there and paste it into the platform?

Check the rules first. Some tests forbid it, and proctored tests can record tab switches and pasting, which a reviewer may read as something worse. Where it's allowed, it still has costs: you'll be copying back and forth to run tests, and the platform's compiler or language version may differ from yours.

The safe default is to practise in a browser editor until you're comfortable without autocompletion, and to treat your own editor as an option only where the rules say so.

:::

## The checklist

### A week before

1. **Read the invitation** for duration, deadline, question types, languages, rules and any sample test. See [reading the invitation](/interviews/assessments/#reading-the-invitation).
2. **Take the sample test**, if there is one. Try both "run" and "submit", custom input, and what happens when your code throws or doesn't compile.
3. **Find the execution environment.** Each platform documents the language versions it runs and their time and memory limits. For example, HackerRank's [execution environment](https://candidatesupport.hackerrank.com/articles/2201684846-execution-environment) page. Don't assume they match your machine.
4. **Learn the two input styles** in your language; the [coding page](./question-types/coding) shows both, and the [C# toolkit](./csharp-toolkit) has a fast input reader.
5. **Do a timed practice set** from the [practice sets](./practice/), with a real timer and no interruptions.

### The day before

- Check your machine: browser up to date, webcam and microphone working if it's proctored, laptop charged, and a wired or reliable connection.
- Decide when you'll take it. A time when you're rested, with buffer before the deadline in case something goes wrong.
- Have water, paper and a pen ready. Sketching an example by hand is often faster than reasoning in your head.

### Just before you start

- Close everything else: other tabs, chat, notifications, and anything that could make noise or pop up on screen during a proctored test.
- Read the platform's instructions screen properly. It's the last chance to learn the rules without the clock running.

## What not to spend time on

- **Memorising more algorithms the night before.** Recognising when to use a hash map, two pointers or a heap matters more than knowing rare algorithms. The [coding page](./question-types/coding) has a short map of common patterns.
- **Tuning micro-optimisations.** On assessments, the difference between passing and timing out is almost always the complexity of the algorithm, not the speed of the code. See [reading constraints](./reading-constraints).
