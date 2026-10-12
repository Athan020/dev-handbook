# The Engineering Handbook — Build Plan (v2)

> A VitePress site for building **T-shaped** software engineering skills, organised by orientation (frontend or backend). It is meant to help engineers grow in their current role and prepare for multi-round interviews. It teaches judgement: creative problem solving that still respects good practice.

**Plan version:** v2 (journey-based). Supersedes v1, which was topic-based and migration-first. See §9.
**Status:** Phase 1 complete (2026-10-12), awaiting review. Phase 0 complete and approved (2026-10-11).
**Owner:** Athan
**Source material:** `_source/backend-engineer-kb/` is a **quarry, not a blueprint**. See §6.

---

## 1. The core idea

Knowing how to *build* something is different from knowing how it *behaves under pressure*. Senior interviews, and senior work, live in that gap. Reference notes and topic catalogues don't close it, because they cover *what* things are, not *when you'd reach for them, what they break and what comes next*.

So the handbook is organised as **journeys**. Each journey:

- **Starts from something the reader already owns.** For backend, that's a working REST API. For frontend, a working page.
- **Applies pressure:** more traffic, slow work, failures, more teams, attackers.
- **Moves outward one stage at a time.** Each stage's fix creates the problem that triggers the next stage.
- **Ends with a capstone** that is a realistic interview question, answered with everything the journey built up.

This is the T in practice. The journey runs down the reader's core skill and out along the bar of the T.

## 2. Principles

1. **Journeys define scope, not existing notes.** If a journey needs something, it gets written even if no note exists. If no journey needs a note, it stays in `_source/`.
2. **Training, not reference.** Every page asks the reader to think before it gives the answer.
3. **Diagnose before you fix.** Options appear as an escalation ladder, simplest and cheapest first. Readers should not jump to sharding or Kafka.
4. **Trade-offs are the content.** For every technique, cover what it fixes, what it breaks and when *not* to use it.
5. **Correct by construction.** Code compiles and runs. Wrong code appears only on purpose, as a labelled "spot the bug" exercise.
6. **Don't rewrite what the internet already does well.** Basic fundamentals and LeetCode-style pattern drills get a short self-check and links out. The handbook spends its words on the gap between knowing and judging.
7. **Concepts are portable.** Explanations are language-neutral. C# is the primary code language for backend examples, and other languages can come later through VitePress code groups.
8. **Banking is a recurring example domain**, not a target employer. Do not include company- or role-specific framing.
9. **Vendor facts age badly.** Link to official docs for limits, quotas and pricing instead of hard-coding them.

## 3. Decisions

| Decision | Choice | Revisit? |
|---|---|---|
| Static site generator | VitePress (latest stable) | No |
| Diagrams | Mermaid, rendered by our own `MermaidDiagram` theme component (loaded only on pages with diagrams). Replaced `vitepress-plugin-mermaid`, which stalled. Playwright tests check every diagram renders in both themes | Build-time SVG rendering if client-side rendering causes problems |
| Search | VitePress built-in local search | Later, if Algolia is needed |
| Hosting | GitHub Pages via GitHub Actions | Yes, could be Netlify or Vercel |
| Organising unit | Journeys of stages, not topic folders | No |
| Backend code language | C# primary, concepts language-neutral | Yes |
| Source KB | `_source/`, read-only, excluded from the build with `srcExclude`, mined per module | No |
| Runnable code | `samples/` .NET solution plus Docker Compose labs, imported into pages with `<<< @/…` | No |
| Shared concept pages | A `reference/` page exists only once **two or more** journeys need the same concept | No |
| Algorithm practice volume | Teach patterns and assessment skills, then link out to LeetCode and NeetCode | No |
| Progress tracking | Client-side only (localStorage, wrapped in try/catch), no accounts | Yes |
| Site generator, revisited | Stay on VitePress (assessed 2026-10-11): every planned interaction works as a Vue component, including real Postgres in the browser via PGlite (spiked). Astro + Starlight is the alternative | If the frontend track needs many live React demos, VitePress 2 stalls while 1.x needs security fixes, or the site needs accounts |
| Interactivity | Vue components in the VitePress theme, loaded only on pages that use them, each covered by Playwright tests. SQL runs in the browser via PGlite. C# labs stay local in `samples/` (no .NET in the browser) | Yes |

## 4. Information architecture

```
docs/
├── index.md                     # Landing: the idea + "Choose your orientation"
├── guide/
│   ├── t-shaped-skills.md       # The T model: core (stem) vs breadth (bar)
│   ├── how-journeys-work.md     # Stages, triggers, capstones; growth path vs interview path
│   └── module-format.md         # The stage template, explained to readers
├── backend/
│   ├── index.md                 # Backend T map + journey list + suggested order
│   ├── core-self-check.md       # What the journeys assume you own, with self-check questions and links out
│   ├── journeys/
│   │   ├── your-api-gets-popular/
│   │   │   ├── index.md         # Premise, stage map (Mermaid), what you'll be able to do
│   │   │   ├── 01-measure-first.md
│   │   │   └── …
│   │   └── …
│   └── reference/               # Shared concept pages (created only when needed by 2+ journeys)
├── frontend/
│   ├── index.md
│   ├── core-self-check.md
│   └── journeys/                # Outlines only in v1 of the site
└── interviews/                  # Shared across tracks
    ├── index.md                 # Overview and entry point for the Interviews nav link
    ├── stages.md                # What each round tests, and which journeys prepare you for it
    ├── system-design-framework.md
    ├── coding-patterns.md       # Short pattern map, links out
    ├── behavioural.md           # STAR
    └── assessments/             # Online assessments (Phase 3)
```

## 5. Journeys

### Backend

**Premise shared by all journeys:** you own a working REST API for a banking product (accounts and transactions) backed by a relational database. It works in development. Then real life happens.

**B1 — Your API gets popular** *(flagship, Phase 1)*

| # | Stage | Triggered by |
|---|---|---|
| 1 | Measure first: latency percentiles, where time goes (app, database or network), reading a query plan | "The transactions endpoint is slow at peak" |
| 2 | Indexes and query shape: composite, covering, selectivity, SARGable queries | The plan shows a full scan |
| 3 | What is the ORM doing? N+1, over-fetching, offset vs keyset pagination | Indexed, but each request still runs a query per row |
| 4 | Too many connections: pool exhaustion, async all the way, timeouts | Fast queries, but requests queue under load |
| 5 | Cache it, then invalidate it: cache-aside, TTLs, stampedes | The same hot reads, thousands of times a second |
| 6 | Reads outgrow one box: read replicas, replication lag, read-your-writes | Cache misses and uncachable reads still saturate the primary |
| 7 | Writes outgrow one box: partitioning vs sharding, hot keys, stable hashing | The primary is now write-bound |
| ★ | Capstone: "Reads are degrading on a 10M-row table — what do you do?" plus back-of-envelope estimation | — |

**B2 — Work that shouldn't happen in the request** *(flagship, Phase 2)*

| # | Stage | Triggered by |
|---|---|---|
| 1 | Get it out of the request: in-process background work vs a queue | The transfer endpoint also sends an email and calls a fraud check |
| 2 | Queues, topics or streams: picking the shape | More than one service now cares about "transfer completed" |
| 3 | Delivered twice: at-least-once delivery and idempotent consumers | A customer gets two notifications |
| 4 | The event that never got sent: the dual-write problem, outbox and inbox | Saved to the database, crashed before publishing |
| 5 | Order matters, sometimes: partition keys, sessions, per-entity ordering | A "debit" is processed before its "account opened" |
| 6 | Poison messages: retries, backoff, dead-letter queues, replay | One malformed message blocks the queue |
| 7 | The queue keeps growing: backpressure, consumer scaling, lag monitoring | Month-end batch run |
| ★ | Capstone: "Design a multi-channel notification system" | — |

**B3 — The money must add up** *(Phase 4)*: transactions and isolation anomalies (lost update, write skew), optimistic vs pessimistic concurrency, idempotency keys on the API, double-entry ledgers, reconciliation. Capstone: *design a transfer between accounts*.

**B4 — Things fail** *(Phase 4)*: timeouts (connect vs request timeouts, propagating deadlines between services), retries with backoff and jitter, circuit breakers and bulkheads, graceful degradation, rate limiting. Capstone: *design a rate limiter*.

**B5 — One service becomes many** *(Phase 4)*: modular monolith first, boundaries (bounded contexts), data ownership, sync vs async between services, sagas, API gateways, strangler-fig migration. Capstone: *migrate a capability out of a monolith*.

**B6 — Shipping it safely** *(Phase 4)*: triggered by "a deploy took the site down for 20 minutes". A pipeline you can trust (build, test, artifacts), environments and configuration, infrastructure as code (drift, review), database migrations without downtime (expand, then contract), deployment strategies (rolling, blue-green, canary), feature flags, rollbacks. Capstone: *ship a breaking schema change to the transfer API with zero downtime*.

**B7 — Where your code actually runs** *(Phase 4)*: triggered by "it's fast on my laptop and slow in production". Where the latency goes on the path from client to process (DNS, TLS, load balancer), connection reuse and `HttpClient` socket exhaustion, CPU-bound vs I/O-bound work and thread-pool starvation, memory and garbage collection, container resource limits (CPU throttling, out-of-memory kills), VMs vs containers vs serverless (cold starts), scaling up vs out and what autoscaling should react to. Networking fundamentals (HTTP, DNS, TCP) go in the core self-check with links out, not in the stages. Capstone: *"Users in another region see 2-second responses — trace the path"*.

**B8 — Something is wrong in production** *(Phase 4)*: triggered by "support says the transfer page is slow, sometimes". What to instrument and why (logs, metrics, traces), SLOs and alerts that mean something, a troubleshooting method (reproduce, isolate, find what changed), diagnosing performance degradation (USE and RED checklists, profiling with the .NET diagnostic tools), defect triage (severity vs priority, minimal reproduction, regression or long-standing, `git bisect`), running an incident (mitigate first), a postmortem that changes something. Capstone: *"Latency doubled after a deploy — walk me through it"*.

**B9 — Someone is trying to break it** *(Phase 4)*: authentication and authorisation (OAuth 2.0 and OIDC, JWT pitfalls), common web vulnerabilities, secrets handling, network boundaries (building on B7). Capstone: *threat-model the transfer API*.

Stage lists for B3–B9 are drafts. Propose the full stage table for a journey and get it approved before writing it.

### Frontend (outline in Phase 5)

**Premise:** you own a working page or app that calls a backend API.

- **F1 — Your page feels slow:** the rendering pipeline, Core Web Vitals, bundle size, images, caching and CDNs.
- **F2 — The API is flaky:** loading and error states, retries, optimistic UI, client-side data caching.
- **F3 — State gets out of hand:** local vs shared vs server state, and when a state library helps or hurts.
- **F4 — Everyone can use it:** accessibility as an engineering discipline.
- **F5 — Someone injects a script:** XSS, CSP, where tokens live, CSRF.
- **F6 — The app grows a team:** component architecture, design systems, testing strategy.

## 6. Using the source material

`_source/backend-engineer-kb/` holds Athan's earlier Obsidian notes. It is a **quarry**: useful parts get mined for specific stages, and the notes do not define structure or scope.

When writing a stage:

1. Search `_source/` for notes relevant to that stage.
2. Reuse what is **correct and useful**, such as a diagram, a code example or a well-explained point. Rewrite it in the stage's voice. Never paste whole sections or pages.
3. Check reused code against §8 (known issues) and against current .NET behaviour.
4. List the notes consulted in the stage's `sources:` frontmatter.
5. Notes that no journey needs stay in `_source/`. There is no migration step and no requirement to account for every source file.

`_source/backend-engineer-kb/09-system-design-prep/08-troubleshoot-slow-reads.md` is the **tone reference**: it diagnoses before fixing, escalates in sensible order, and ends with the answer you'd give in the interview.

## 7. The stage template

**Frontmatter:**

```yaml
---
title: Too many connections
track: backend            # backend | frontend
journey: your-api-gets-popular
stage: 4                  # 1–7, or `capstone` for the capstone page
level: intermediate       # foundation | intermediate | advanced
sources: [05-databases/query-optimization.md]   # _source notes consulted, if any
status: draft             # draft | written | reviewed
---
```

**Sections (in order):**

1. **Where we are.** One or two sentences recapping the previous stage's fix and the new symptom it led to (the trigger).
2. **Stop and think.** Two to four questions, each in a `::: think Question?` block. The reader writes an answer before the model answer (the block's body) is revealed.
3. **The mental model.** First principles, with a Mermaid diagram where structure matters.
4. **The escalation ladder.** Options from cheapest to most complex. For each: what it fixes, what it costs, what it breaks.
5. **Trade-offs at a glance.** One table.
6. **Traps.** Common mistakes, including at least one "spot the bug" sample in a `::: trap` block.
7. **In the interview.** How it's usually asked, a short model answer, and three or more follow-ups with hidden answers in a `::: interview` block.
8. **Try it.** A lab in `samples/` (Docker Compose where useful), a design prompt or a code fix.
9. **What breaks next.** The hook into the next stage.

Author templates: `docs/_templates/stage.md` and `docs/_templates/capstone.md` (excluded from the build). Nest `::: details` inside `::: trap` and `::: interview` by giving the outer block more colons (`:::::`).

Journey `index.md` pages include the premise, a Mermaid stage map, prerequisites from the core self-check, and what the reader will be able to explain at the end. Capstone pages follow the interview flow (clarify, estimate, design, deep dive) and link back to the stage that covers each decision.

## 8. Known issues in the source material

Do not carry these over as correct. Use them as "spot the bug" material where they fit:

| File | Issue | Natural home |
|---|---|---|
| `05-databases/database-scaling-deep-dive.md` — `ShardRouter` | Uses `string.GetHashCode()`, which is randomised per process in modern .NET, so routing changes between restarts. Modulo hashing also makes adding shards expensive. | B1 stage 7 |
| `03-architecture/event-driven-architecture.md` — `TransactionService`, `EventSourcedAccountRepository` | Saves to the database and then publishes, so a crash in between loses the event (dual write). | B2 stage 4 |
| `06-cloud/messaging-events.md` — `EnqueueBatchAsync` | Reassigns a `using var` variable, which does not compile. | B2 (minor trap) |
| `02-system-design/idempotency.md` — `PaymentService` | Debits and credits across services with no transaction or compensation, and stores the idempotency result in a cache separately from the database write. | B3 |

## 9. Changes from v1

- **Removed:** the Obsidian migration phase, `scripts/migrate-obsidian.mjs`, the `MIGRATION.md` report, the topic-folder structure (`core/`, `breadth/`) and the source-to-target mapping table.
- **Changed:** the site is organised by journeys of stages. `_source/` is mined per stage instead of migrated. Phases are renumbered.
- **Kept:** VitePress scaffolding, Mermaid, the custom containers, `samples/`, the stage template (adapted from the module template), the online assessment section and the known-issues list.
- **If v1 Phase 1 work exists:** stop it. Remove the migration script, `MIGRATION.md` and any migrated pages under `docs/`. Confirm `_source/` is unmodified (`git diff --stat -- _source` should be empty). Commit the cleanup on its own.

## 10. Phases

Each phase ends with a review stop. Claude Code finishes the phase, summarises what changed, lists anything uncertain, and waits for approval before starting the next one.

### Phase 0 — Scaffold (adjust to v2)

- Repo setup: `package.json`, VitePress, the Mermaid plugin, `.gitignore`, `.editorconfig`.
- Create the `docs/` structure from §4 with stub pages. For backend, add stub journey folders for B1 and B2 only, each with an `index.md` that shows the premise and stage table from §5. List the later journeys (now B3–B9) on the backend index as "coming later". Do not create stub pages for them.
- Landing page: VitePress home layout with hero actions **Backend** and **Frontend**, and a short explanation of the journey idea and the T model.
- Sidebars per section (`/backend/`, `/frontend/`, `/interviews/`, `/guide/`), with backend journeys grouped and stages in order.
- Custom containers `interview` and `trap`.
- Local search enabled.
- GitHub Actions workflow to build and deploy to GitHub Pages.
- `srcExclude` covering `_source/**` and `samples/**`.

**Done when:** `npm run docs:dev` serves the site, `npm run docs:build` passes with no dead links, the landing page routes to both tracks, and the B1 and B2 journey indexes show their stage maps.

### Phase 1 — Stage template and journey B1 "Your API gets popular"

1. Write `docs/guide/how-journeys-work.md`, `docs/guide/module-format.md` and `docs/_templates/stage.md`.
2. Create `samples/` as a .NET 10 (LTS) solution: a small transactions API (EF Core plus Postgres in Docker Compose) seeded with enough data, millions of rows, to make performance problems real. Each stage's lab changes or measures this one app.
3. Write `backend/core-self-check.md` covering what B1 assumes (HTTP and REST, SQL basics, async/await), with links out.
4. Build the interactive components B1 needs, each with Playwright tests:
   - **Commit-then-reveal** for stop-and-think questions: the reader writes an answer before the reveal opens. Once built, update §7 and `CLAUDE.md` so stop-and-think uses it instead of `::: details`.
   - **SQL playground** (PGlite, real Postgres in the browser) for stages 1–3 and 7: run queries and read real `EXPLAIN` plans against seeded data. It is about 16 MB before compression and loads only on pages that use it, so keep the seed small and show a loading state.
   - **Simulators** (plain Vue, no libraries) where watching the mechanism helps: connection-pool queueing (stage 4), cache TTLs and stampedes (stage 5), replication lag and read-your-writes (stage 6), the consistent-hashing ring (stage 7).
5. Write B1 stages 1–7 and the capstone, following §7 and mining `_source/` per §6.

**Done when:** every B1 page follows the template, the labs run with `docker compose up` and `dotnet run` or `dotnet test`, each stage's "What breaks next" leads into the following stage, and every interactive component passes its Playwright tests.

### Phase 2 — Journey B2 "Work that shouldn't happen in the request"

- Extend the same sample app with RabbitMQ in Docker Compose. Use the raw client or a thin wrapper so the mechanics stay visible. A framework like MassTransit can be mentioned as the production choice.
- Write B2 stages 1–7 and the capstone.
- Simulators where they help, reusing the Phase 1 component patterns: duplicate delivery and idempotent consumers (stage 3), the outbox (stage 4), backlog growth and backpressure (stage 7).
- Include the dual-write bug from §8 as the stage 4 trap, and build the outbox fix in the lab.

**Done when:** same bar as Phase 1.

### Phase 3 — Online assessment prep

Company online assessments (OAs) are often the first technical gate, and they test differently from live interviews. There is no interviewer. Submissions are scored automatically against hidden test cases, often with partial credit, under a time limit and sometimes in a proctored or secure mode. Each company configures its own mix of question types.

The goal is not another LeetCode guide. It covers the skills OAs reward that algorithm drills don't.

```
interviews/assessments/
├── index.md                 # What OAs are; HackerRank vs Codility vs CodeSignal from a candidate's view; reading the invite
├── before-the-test.md       # Practise in the platform IDE, language setup, environment check
├── test-day-strategy.md     # Budget by points, bank partial credit, write edge cases, know when to move on
├── reading-constraints.md   # Input size → target complexity, with an exercise
├── question-types/
│   ├── coding.md            # Function stubs vs stdin/stdout; why hidden tests fail
│   ├── sql.md               # Joins, grouping, window functions, NULLs, dialects
│   ├── projects.md          # Multi-file tasks scored by tests: read fast, run tests first, small changes
│   ├── code-review-and-debugging.md   # Reuse "spot the bug" material from the journeys
│   └── multiple-choice.md
├── csharp-toolkit.md        # Fast input, overflow, PriorityQueue, SortedSet, sorting, recursion limits, LINQ traps
└── practice/                # 3–5 timed sets; harnesses in samples/assessments/ (xUnit) with visible + hidden-style tests
```

Link to each platform's official candidate help pages instead of restating their rules. Frontend-specific OA content goes to Phase 5.

**Done when:** every page exists, the practice harnesses run with `dotnet test`, and nothing duplicates standard LeetCode pattern guides beyond a short pattern map that links out.

### Phase 4 — Backend journeys B3–B9

Order: **B3 → B4 → B5 → B6 → B7 → B8 → B9.** For each journey, first propose the full stage table and capstone and get approval, then write it. Create `backend/reference/` pages only when two or more journeys need the same concept, for example idempotency (B2, B3, B4) and a troubleshooting method (B1, B4, B7, B8). Where they help, B7 and B8 use the Phase 1 interactive patterns: a container-throttling simulator, an "investigate this incident" exercise where the reader chooses what to check next, and a defect-triage ranking exercise. Add networking basics (HTTP, DNS, TCP) to `backend/core-self-check.md` before writing B7.

**Done when:** all backend journeys are written and the interview stages page links each round to the journeys that prepare for it, including debugging and production-support rounds (B7, B8).

### Phase 5 — Frontend track (outline)

- Draft the frontend T map, `frontend/core-self-check.md`, and a stage table for F1–F6.
- Write F1 as a full journey only after Athan approves the outline.
- Outline frontend online-assessment content (framework tasks in the platform IDE, DOM and JavaScript output-prediction questions) and link it from `interviews/assessments/`.

### Phase 6 — Interview layer and polish

- Finish the `interviews/` pages, cross-linked to journey capstones and stages.
- Per-journey progress checklists (localStorage, try/catch, works without storage).
- Two suggested paths per track: **growth** (journeys in order) and **interview in two weeks** (capstones first, then the stages they lean on, plus assessments).
- Accessibility and mobile pass, and a link checker in CI.

## 11. Open questions

- Hosting: GitHub Pages, or Netlify/Vercel with preview deploys?
- Will the site be public? This affects licensing and how much personal context is included.
- Should there be a fullstack track, or do cross-links between journeys cover it?
- Add other backend languages (Java, Go, Python) through code groups, and if so, when?
