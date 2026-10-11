---
layout: home
title: The Engineering Handbook
track: shared
status: draft

hero:
  name: The Engineering Handbook
  text: Build T-shaped engineering skills
  tagline: Start from something you already own, apply pressure, and follow it outward. Learn to judge trade-offs, diagnose before you fix, and explain your reasoning under interview pressure.
  actions:
    - theme: brand
      text: Backend
      link: /backend/
    - theme: alt
      text: Frontend
      link: /frontend/
    - theme: alt
      text: How journeys work
      link: /guide/how-journeys-work

features:
  - title: Journeys, not topics
    details: Each journey starts from something you already own and moves outward one stage at a time. Every fix creates the next problem.
  - title: Diagnose before you fix
    details: Options are laid out as an escalation ladder, cheapest and simplest first, with what each one breaks.
  - title: Ready for the interview
    details: Every journey ends with a capstone, a realistic interview question answered with everything the journey built up.
---

## How it works

Knowing how to build something is different from knowing how it behaves under pressure. Senior work, and senior interviews, live in that gap.

So the handbook is organised as **journeys**. Each one starts from something you already own, such as a working REST API or a working page. Then it applies pressure: more traffic, slow work, failures, more teams, attackers. Each stage fixes the problem in front of you, and that fix creates the problem that triggers the next stage. The journey ends with a capstone: a realistic interview question.

```mermaid
flowchart LR
  OWN(["What you own"]) -->|pressure| S1["Stage 1"]
  S1 -->|"the fix creates the next problem"| S2["Stage 2"]
  S2 --> MORE["…"]
  MORE --> CAP(["★ Capstone"])
```

## The T model

A T-shaped engineer has depth in one area and working knowledge of the areas around it. The vertical stroke of the T is your **core**: the skills your role expects you to own. The horizontal bar is your **breadth**: the systems your work depends on, which you need to reason about even if you don't build them every day.

A journey runs down your core and out along the bar. Pick the track closest to your current role: [backend](/backend/) or [frontend](/frontend/). Shared interview preparation lives under [Interviews](/interviews/).
