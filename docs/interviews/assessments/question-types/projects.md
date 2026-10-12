---
title: Project tasks
track: shared
status: written
---

# Project tasks

Instead of a single function, you get a small codebase: a dozen files, a README with a task ("transfers between currencies are charged the wrong fee; also add a fee cap for premium customers"), and a test suite. Your score is the share of tests that pass at the end, including tests you can't see. These tasks are closer to real work than algorithm questions, and they reward a different discipline.

## Stop and think

::: think You have 60 minutes and an unfamiliar codebase. What do you do in the first ten?

Not write code. In roughly this order:

1. **Read the task** in full, and note each thing it asks for. They often map one-to-one onto groups of tests.
2. **Run the tests** before changing anything. It tells you that the project builds, how long the tests take, and which ones fail now. The failing tests are your specification.
3. **Read the failing tests**, not the whole codebase. Each one names the behaviour it wants and the class it calls, which shows you where to start.
4. **Skim the structure**: the main types, where the logic lives, and anything an instructions file says you mustn't change.

Ten minutes of this usually saves thirty minutes of wandering.

:::

::: think You spot a badly designed class that makes the task awkward. Do you refactor it?

No. The tests, visible and hidden, were written against the existing design. A refactor risks breaking tests you can't see, takes time that earns no points, and makes your changes harder to review if a person reads them later. Make the smallest change that does what the task asks, in the style of the code around it. If the design really is a problem, a short note in a comment or the submission is enough.

:::

## How to work

- **Keep it compiling.** Build and run the tests after every small change. A project that doesn't compile at the end scores nothing, however close it was.
- **One task at a time.** Finish a requirement, see its tests pass, then move on. Partial credit comes from complete requirements, not from five half-done ones.
- **Follow the existing patterns.** If fees are calculated by rule classes, add a rule class. Hidden tests often exercise the same mechanism in ways the visible ones don't.
- **Write a test when you're unsure.** If the task says "fees are capped at 50" and no visible test covers exactly 50, add one. It costs a minute and catches the boundary bug the hidden tests are looking for.
- **Don't change the tests, or anything marked read-only.** The scoring uses its own copies, so changing them helps nothing, and it can look bad to a reviewer.
- **Watch the environment.** Platforms often provide a browser-based editor with the dependencies installed. Read the platform's notes on how to run the tests, and on what's evaluated if time runs out. HackerRank's are in [its candidate article on project assessments](https://candidatesupport.hackerrank.com/articles/8606305957-taking-front-end-back-end-full-stack-and-mobile-developer-assessments) and [debugging in projects](https://candidatesupport.hackerrank.com/articles/2607395746-debugging-in-projects-and-code-repositories).

## Reading a failing test

A failing test is the fastest route into unfamiliar code. Read three things:

1. **The name**: what behaviour is expected, in words.
2. **The arrangement**: which objects it builds, and with what values. This tells you which class to open.
3. **The failure message**: expected against actual. A value that's off by a little suggests rounding or a boundary; a value that's wildly off suggests the wrong rule or the wrong branch; an exception points straight at a line.

Then set a breakpoint or add a temporary log line in the code under test, and run just that test.

## Practise

[Practice set 4](../practice/#set-4-project-task) is a small fee-calculation codebase with failing tests to fix and a feature to add, scored by visible and hidden tests.
