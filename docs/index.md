---
layout: home
title: The Engineering Handbook
track: shared
level: foundation
status: draft

hero:
  name: The Engineering Handbook
  text: Build T-shaped engineering skills
  tagline: Start from what you already know and push outward. Learn to judge trade-offs, diagnose before you fix, and explain your reasoning under interview pressure.
  actions:
    - theme: brand
      text: Backend
      link: /backend/
    - theme: alt
      text: Frontend
      link: /frontend/
    - theme: alt
      text: What is a T-shaped engineer?
      link: /guide/t-shaped-skills

features:
  - title: Training, not reference
    details: Every module opens with a scenario and asks you to think before it gives the answer.
  - title: Diagnose before you fix
    details: Options are laid out as an escalation ladder, cheapest and simplest first, with what each one breaks.
  - title: Ready for the interview
    details: Each module ends with how the question is usually asked, a model answer and likely follow-ups.
---

## Choose your orientation

A T-shaped engineer has depth in one area and working knowledge of the areas around it. The vertical stroke of the T is your **core**: the skills your role expects you to own. The horizontal bar is your **breadth**: the systems your work depends on, which you need to reason about even if you don't build them every day.

```mermaid
flowchart TB
  subgraph Breadth["Breadth: the bar"]
    direction LR
    B1[Databases at scale] --- B2[Messaging] --- B3[Caching] --- B4[Distributed systems]
  end
  Breadth --- Core
  subgraph Core["Core: the stem"]
    direction TB
    C1[API design] --- C2[Data modelling and SQL] --- C3[Your stack in depth]
  end
```

Each track is organised the same way. Pick the one closest to your current role: the [backend track](/backend/) or the [frontend track](/frontend/). Shared interview preparation lives under [Interviews](/interviews/).
