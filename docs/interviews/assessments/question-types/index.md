---
title: Question types
track: shared
status: written
---

# Question types

Employers choose the mix of questions in an assessment, and the invitation usually says which kinds to expect. Each kind is scored differently, and fails for different reasons.

| Type | What you get | How it's scored | Most often lost to |
|---|---|---|---|
| [Coding](./coding) | A problem, examples, constraints and an editor | Hidden tests, each pass or fail, often weighted | Edge cases, overflow, timeouts on large inputs |
| [SQL](./sql) | A schema, sample data and a question | The query's result compared with the expected rows | NULLs, joins that drop rows, ties, ordering |
| [Project tasks](./projects) | A small codebase, a task and a test suite | The share of tests, visible and hidden, that pass | Running out of time exploring, or ending with code that doesn't compile |
| [Code review and debugging](./code-review-and-debugging) | Existing code with a bug, or a change to review | Tests on the fixed code, or a person reading your comments | Rewriting instead of fixing; reviewing style instead of correctness |
| [Multiple choice](./multiple-choice) | A question and several options | Per question, sometimes with a penalty for wrong answers | Reading too fast |

Whatever the type, the same habits apply: read the whole question before starting, work out from the constraints what's expected, test the cases the examples don't show, and keep something that works at all times. [Test-day strategy](../test-day-strategy) has the details.
