---
title: Stage title, stated as the problem (e.g. "Too many connections")
track: backend                 # backend | frontend
journey: your-api-gets-popular # journey folder name
stage: 1                       # 1–7 (capstones use capstone.md)
level: intermediate            # foundation | intermediate | advanced
sources: []                    # _source notes consulted, e.g. [05-databases/query-optimization.md]
status: draft                  # draft | written | reviewed
---

<!--
Author template for a journey stage. Copy it to docs/<track>/journeys/<journey>/NN-slug.md.
This folder is excluded from the build.

Before writing:
- Search _source/ for notes on this stage. Reuse only what is correct, rewrite it in this
  voice, check code against PLAN.md §8, and list the notes in `sources`.
- Tone reference: _source/backend-engineer-kb/09-system-design-prep/08-troubleshoot-slow-reads.md
- Plain, direct prose. No filler, marketing tone or emoji headings. UK spelling.
- C# for backend code. All code compiles, except inside a `::: trap` block.
- Don't hard-code vendor limits, quotas or prices; link to official docs.
- Fundamentals: a line and a link out, not a re-teach.
-->

# N. Stage title

## Where we are

<!-- One or two sentences: what the previous stage fixed, and the new symptom it led to.
     Use the trigger from the journey's stage table, as something the reader would see. -->

## Stop and think

<!-- Two to four questions. The reader writes an answer before the model answer is revealed.
     Ask about judgement (what would you check first, what could go wrong), not definitions. -->

::: think First question?

Model answer.

:::

::: think Second question?

Model answer.

:::

## The mental model

<!-- First principles: how the thing actually works, in language-neutral terms.
     Add a Mermaid diagram where structure or flow matters. -->

## The escalation ladder

<!-- Options from simplest and cheapest to most complex. For each rung: what it fixes,
     what it costs, what it breaks, and when not to use it. Say what tells you to move
     up a rung. Show code where it helps (compiling C#, or a snippet from samples/). -->

### 1. Simplest option

**Fixes:** …
**Costs:** …
**Breaks:** …
**Don't use it when:** …

### 2. Next option

…

## Trade-offs at a glance

| Option | Fixes | Costs | Breaks | Reach for it when |
|---|---|---|---|---|
| … | … | … | … | … |

## Traps

<!-- Common mistakes, with at least one "spot the bug" exercise. Use PLAN.md §8 bugs where they fit.
     Nest `::: details` inside with more colons on the outer block. -->

::::: trap
Context for the code, and the question: what's wrong with it?

```csharp
// Wrong code goes here, and only here.
```

::: details Answer
What's wrong, why it matters, and the fix.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "…"

**Short answer:** two to four sentences you could say out loud.

::: details Follow-up: …?
Answer.
:::

::: details Follow-up: …?
Answer.
:::

::: details Follow-up: …?
Answer.
:::
:::::

## Try it

<!-- A lab against the sample app in samples/ (Docker Compose where useful), a design
     prompt, or a code fix. Give the exact commands, and say what the reader should see. -->

## What breaks next

<!-- The problem this stage's fix creates. It must lead into the next stage's trigger.
     End with a link to the next stage. -->
