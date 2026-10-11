# The Engineering Handbook — Build Plan

> A VitePress site for building **T-shaped** software engineering skills, organised by orientation (frontend or backend). It is meant to help engineers grow in their current role and prepare for multi-round interviews. It teaches judgement: creative problem solving that still respects good practice.

**Status:** Phase 0 complete — awaiting review
**Owner:** Athan
**Source material:** `_source/backend-engineer-kb/` (an existing Obsidian knowledge base, used as reference only and never published as-is)

---

## 1. Why this exists

The handbook is built from a real gap. Knowing how to *build* a REST API is different from knowing how it *behaves under pressure*. Interviews at senior level push past the request layer into database scaling, performance and asynchronous messaging. That is where reference-style notes fall short.

Each module therefore **starts from something the reader already knows and pushes outward**: "your endpoint now gets 50x the traffic — what breaks first?"

## 2. Principles

1. **Training, not reference.** Every page asks the reader to think before it gives the answer.
2. **Diagnose before you fix.** Options are presented as an escalation ladder, cheapest and simplest first. Readers should not jump to sharding or Kafka.
3. **Trade-offs are the content.** For every technique, cover what it fixes, what it breaks and when *not* to use it.
4. **Correct by construction.** Code samples must compile and run. Wrong code appears only on purpose, clearly labelled as a "spot the bug" exercise.
5. **Concepts are portable.** Explanations are language-neutral. C# is the primary code language for backend examples, and other languages can be added later with VitePress code groups.
6. **Banking is a recurring example domain**, not a target employer. Do not include role- or company-specific framing.
7. **Vendor facts age badly.** Link to official docs for limits, quotas and pricing instead of hard-coding numbers.

## 3. Decisions

| Decision | Choice | Revisit? |
|---|---|---|
| Static site generator | VitePress (latest stable) | No |
| Diagrams | Mermaid via `vitepress-plugin-mermaid` | Only if it breaks on the current VitePress |
| Search | VitePress built-in local search | Later, if Algolia is needed |
| Hosting | GitHub Pages via GitHub Actions | Yes, could be Netlify or Vercel |
| Backend code language | C# primary, concepts language-neutral | Yes |
| Source KB location | `_source/`, excluded from the build with `srcExclude` | No |
| Runnable code | Flagship modules keep code in `samples/` (a .NET solution) and import snippets with `<<< @/…` | No |
| Progress tracking | Client-side only (localStorage), no accounts | Yes |
| Algorithm practice volume | Do not duplicate LeetCode guides. Teach the patterns and assessment skills, then link out to LeetCode and NeetCode for practice volume | No |
| Assessment practice | Practice problems ship with local test harnesses in `samples/assessments/` that include visible and hidden-style edge-case tests | No |

## 4. Information architecture

```
docs/
├── index.md                 # Landing: what this is + "Choose your orientation" (Frontend / Backend)
├── guide/
│   ├── t-shaped-skills.md   # The T model: core (stem) vs breadth (bar)
│   ├── how-to-use.md        # Growth path vs interview-prep path
│   └── module-format.md     # The module template, explained to readers
├── backend/
│   ├── index.md             # Track overview + the backend T map
│   ├── core/                # The stem: what a backend engineer is expected to own
│   │   ├── api-design/
│   │   ├── data-modelling-and-sql/
│   │   ├── auth/
│   │   ├── code-design/     # SOLID, design patterns
│   │   ├── dotnet/          # Stack depth: async/await, DI, memory, concurrency
│   │   └── delivery/        # Testing, version control, CI/CD, SDLC
│   ├── breadth/             # The bar: what surrounds the core and comes up in interviews
│   │   ├── databases-at-scale/   ← flagship
│   │   ├── messaging/            ← flagship
│   │   ├── caching/
│   │   ├── distributed-systems/
│   │   ├── architecture/         # Microservices, CQRS, API gateways, DDD
│   │   ├── cloud-and-infra/      # Containers, Kubernetes, IaC, serverless, autoscaling
│   │   ├── observability/
│   │   ├── networking/
│   │   └── security/
│   └── scenarios/           # Full system-design walkthroughs that combine modules
├── frontend/
│   ├── index.md             # Track overview + planned T map (outline only in v1)
│   ├── core/
│   └── breadth/
└── interviews/              # Shared across tracks
    ├── stages.md            # What each round tests
    ├── system-design-framework.md
    ├── coding-patterns.md
    ├── behavioural.md       # STAR
    └── assessments/         # Online assessments (HackerRank, Codility, CodeSignal)
        ├── index.md         # What OAs are and how they differ from live interviews
        ├── before-the-test.md
        ├── test-day-strategy.md
        ├── reading-constraints.md
        ├── question-types/
        │   ├── coding.md        # Function stubs vs stdin/stdout, hidden tests, partial credit
        │   ├── sql.md
        │   ├── projects.md      # Multi-file project tasks scored by tests
        │   ├── code-review-and-debugging.md
        │   └── multiple-choice.md
        ├── csharp-toolkit.md    # Language reference for timed tests
        └── practice/            # Timed practice sets with local test harnesses
```

### Source → target mapping

| Source (`_source/backend-engineer-kb/`) | Target |
|---|---|
| `01-fundamentals/cap-theorem` | `backend/breadth/distributed-systems/` |
| `01-fundamentals/design-patterns`, `solid-principles` | `backend/core/code-design/` |
| `01-fundamentals/algorithms-data-structures` | `interviews/coding-patterns` + `interviews/assessments/` (split) |
| `01-fundamentals/sdlc` | `backend/core/delivery/` |
| `02-system-design/scalability` | `backend/breadth/databases-at-scale/` + `caching/` (split) |
| `02-system-design/distributed-systems`, `redundancy-fault-tolerance`, `consistency-patterns` | `backend/breadth/distributed-systems/` |
| `02-system-design/idempotency` | `backend/breadth/messaging/` (consumer side) + `backend/core/api-design/` (idempotency keys) |
| `03-architecture/event-driven-architecture` | `backend/breadth/messaging/` |
| `03-architecture/microservices`, `n-tier`, `cqrs-event-sourcing`, `api-gateway-patterns` | `backend/breadth/architecture/` |
| `04-dotnet/*` | `backend/core/dotnet/` |
| `05-databases/sql-fundamentals` | `backend/core/data-modelling-and-sql/` |
| `05-databases/nosql-paradigms`, `database-selection`, `query-optimization`, `database-scaling-deep-dive` | `backend/breadth/databases-at-scale/` |
| `06-cloud/messaging-events` | `backend/breadth/messaging/` (vendor appendix) |
| `06-cloud/monitoring-observability` | `backend/breadth/observability/` |
| `06-cloud/networking-security`, `security-services` | `backend/breadth/security/` |
| `06-cloud/*` (remaining) | `backend/breadth/cloud-and-infra/` |
| `07-devops/containerization`, `terraform` | `backend/breadth/cloud-and-infra/` (merge with `06-cloud/containerization-platforms`) |
| `07-devops/ci-cd-pipelines`, `testing-strategies`, `version-control` | `backend/core/delivery/` |
| `08-synthesis/*` | `backend/scenarios/` |
| `09-system-design-prep/0x-*` scenarios | `backend/scenarios/` |
| `09-system-design-prep/05-cheatsheet`, `06-decision-guides` | `interviews/system-design-framework` + per-module "decision" sections |
| `10-interview-prep/interview-guide` | `interviews/` (strip company-specific content) |
| `10-interview-prep/networking-fundamentals`, `firewalls` | `backend/breadth/networking/` |
| `10-interview-prep/security-basics`, `authentication-authorization` | `backend/core/auth/` + `backend/breadth/security/` |
| `11-domain-driven-design/*` | `backend/breadth/architecture/ddd/` |
| `12-data-intensive-apps/storage-retrieval`, `replication-partitioning`, `data-models-query-languages` | `backend/breadth/databases-at-scale/` |
| `12-data-intensive-apps/distributed-data` | `backend/breadth/distributed-systems/` |
| `00-index` | Not migrated (replaced by the landing page and track overviews) |

## 5. The module template

`09-system-design-prep/08-troubleshoot-slow-reads.md` is the reference example of the tone and flow.

**Frontmatter:**

```yaml
---
title: Read replicas and replication lag
track: backend          # backend | frontend
layer: breadth          # core | breadth
level: intermediate     # foundation | intermediate | advanced
prerequisites: [backend/breadth/databases-at-scale/indexing]
status: draft           # draft | migrated | rewritten | reviewed
---
```

**Sections (in order):**

1. **The scenario.** A concrete situation that starts from familiar ground.
2. **Stop and think.** Two to four questions. Answers go in `::: details` blocks so the reader commits first.
3. **The mental model.** First principles, with a Mermaid diagram where structure matters.
4. **The escalation ladder.** Options from cheapest to most complex. For each: what it fixes, what it costs, what it breaks.
5. **Trade-offs at a glance.** One table.
6. **Traps.** Common mistakes, including at least one "spot the bug" code sample in a `::: warning` block.
7. **In the interview.** How the question is usually phrased, a short model answer, and three or more likely follow-ups with hidden answers.
8. **Exercise.** Something to do: a lab in `samples/` (Docker Compose where useful), a design prompt or a code fix.
9. **Related modules.**

**Custom containers:** add `::: interview` and `::: trap` as named custom containers in the VitePress config, alongside the built-in `tip`, `warning` and `details`.

## 6. Phases

Each phase ends with a review stop. Claude Code finishes the phase, summarises what changed, lists anything uncertain, and waits for approval before starting the next phase.

### Phase 0 — Scaffold

- Initialise the repo: `package.json`, VitePress, the Mermaid plugin, `.gitignore`, `.editorconfig`.
- Create the `docs/` structure from section 4, with stub `index.md` pages so every sidebar entry resolves.
- Landing page: VitePress home layout with hero actions **Backend** and **Frontend**, plus a short explanation of the T model.
- Per-track sidebars (`/backend/`, `/frontend/`, `/interviews/`, `/guide/`).
- Custom containers `interview` and `trap`.
- Local search enabled.
- GitHub Actions workflow to build and deploy to GitHub Pages.
- `srcExclude` covering `_source/**` and `samples/**`.

**Done when:** `npm run docs:dev` serves the site, `npm run docs:build` passes with no dead links, and the landing page routes to both tracks.

### Phase 1 — Migrate the source KB

- Write `scripts/migrate-obsidian.mjs`. Do not hand-edit files for this step. The script should:
  - Convert `[[path|Label]]` and `[[path]]` wiki-links to relative Markdown links, resolved against the mapping table above.
  - Convert `> [!TIP]`, `> [!NOTE]` and `> [!WARNING]` callouts to `::: tip`, `::: info` and `::: warning`.
  - Strip Mermaid `click` directives.
  - Add frontmatter with `status: migrated`, `track`, `layer`.
  - Report unresolved links and duplicates instead of guessing.
- Move content into the target structure from the mapping table, merging duplicate files where noted.
- Remove company- and role-specific content (the Capitec requirements table, company values, careers links).
- Leave the content itself unchanged apart from syntax. Rewriting happens in later phases.

**Done when:** the build passes with no dead links, every source file is accounted for in a `MIGRATION.md` report (migrated, merged or dropped, with a reason), and nothing under `_source/` has been modified.

### Phase 2 — Template and flagship modules

1. Write `docs/guide/module-format.md` (reader-facing) and `docs/_templates/module.md` (author-facing).
2. Create `samples/` as a .NET 8 solution for runnable snippets, with Docker Compose for Postgres and a message broker (RabbitMQ).
3. Rewrite **Databases at scale** as a set of modules:
   - Reading query plans and indexing (composite, covering, selectivity, SARGable queries)
   - N+1 queries and ORM pitfalls
   - Connection pooling and exhaustion
   - Caching patterns and invalidation (or link to `caching/`)
   - Read replicas and replication lag (read-your-writes)
   - Transactions, isolation levels and locking
   - Partitioning vs sharding (hash, range, directory, consistent hashing, hot keys)
   - Capacity estimation: back-of-envelope numbers
   - Scenario: "Slow reads on a 10M-row table" (rewrite of the existing piece)
4. Rewrite **Messaging** as a set of modules:
   - Why queues at all (decoupling, load levelling, async work)
   - Queues vs pub/sub vs event streams
   - Delivery guarantees and idempotent consumers
   - Ordering and partition keys
   - Retries, dead-letter queues and poison messages
   - Backpressure and consumer scaling
   - The dual-write problem: transactional outbox and inbox
   - Sagas: choreography vs orchestration
   - Appendix: managed services (SQS/SNS, Service Bus, Kafka, Event Hubs). Keep it short and link to vendor docs.
5. Turn the known bugs in section 7 into "spot the bug" exercises.

**Done when:** both module sets follow the template, all code compiles in `samples/`, and every module has stop-and-think questions, interview follow-ups and an exercise.

### Phase 3 — Online assessment prep

Company online assessments (OAs) are often the first technical gate, and they test differently from live interviews. There is no interviewer to steer you. Submissions are scored automatically against hidden test cases, often with partial credit per test, under a time limit and sometimes in a proctored or secure mode. Each company configures its own mix of question types, so the invite is the best guide to what's coming.

The goal of this section is not another LeetCode guide. It covers the skills OAs reward that algorithm drills don't.

1. **`index.md`.** What an OA is, how HackerRank, Codility and CodeSignal differ from a candidate's point of view, and how to read an invite (duration, question count, languages, question types, proctoring). Link to each platform's official candidate help pages. Do not restate their rules, because they change.
2. **`before-the-test.md`.** Practise in the platform's own IDE (most offer a sample test), set your language and editor preferences, do an environment check, and prepare a short language toolkit.
3. **`test-day-strategy.md`.**
   - Read every question first, then budget time by points, not by order.
   - Bank partial credit: a correct brute force that passes the small tests beats an unfinished optimal solution.
   - Write your own edge-case inputs before submitting (empty input, single element, duplicates, negatives, maximum sizes, overflow).
   - Know when to move on.
4. **`reading-constraints.md`.** How input size tells you the target complexity, for example roughly n ≤ 10³ allows O(n²), n ≤ 10⁵ needs O(n log n), and n ≤ 10⁶ or more needs O(n). Include an exercise: given only the constraints, name the likely approach.
5. **Question types:**
   - *Coding:* function-stub problems vs full stdin/stdout parsing, and why hidden tests fail (overflow, off-by-one, timeouts, unhandled edge cases).
   - *SQL:* joins, grouping and `HAVING`, window functions, `NULL` handling, and dialect differences (SQL Server vs MySQL vs Postgres). This links to `backend/core/data-modelling-and-sql/`.
   - *Projects:* multi-file tasks in a pre-configured IDE, such as adding an endpoint to an existing API and making the provided tests pass. Cover reading an unfamiliar codebase fast, running the tests first, and making small changes. This links to `backend/core/api-design/`.
   - *Code review and debugging:* finding and fixing bugs in someone else's code. Reuse the "spot the bug" exercises from Phase 2.
   - *Multiple choice:* language and framework trivia, output prediction and complexity questions.
6. **`csharp-toolkit.md`.** Fast input parsing (`Console.In`, `StreamReader`, `Split` and `TryParse`), `long` vs `int` overflow, `PriorityQueue<TElement,TPriority>`, `SortedSet`, `Dictionary` patterns, `Array.Sort` with comparers, recursion depth limits and how to rewrite iteratively, and the LINQ performance traps that matter under time limits. Leave room for other languages through code groups.
7. **Practice sets.** Three to five timed sets, each mixing question types and sized like a real OA. Each problem has a local test harness in `samples/assessments/` (xUnit) with visible tests and a separate set of hidden-style edge-case tests, so the reader experiences "passes the examples, fails the hidden tests" and learns from it. Point to LeetCode and NeetCode for additional pattern practice.
8. **Frontend note.** Frontend OAs often include framework tasks in the IDE. Cover these in the frontend track (Phase 5) and link from here.

**Done when:** every page above exists and follows the template where it applies, the practice harnesses run with `dotnet test`, and nothing duplicates standard LeetCode pattern guides beyond a short pattern map that links out.

### Phase 4 — Remaining backend breadth

Rewrite in this order: distributed systems → caching → architecture (including DDD) → observability → security → networking → cloud and infra. Then core: API design → data modelling and SQL → .NET → auth → code design → delivery. Then scenarios.

**Done when:** no backend page is at `status: migrated`.

### Phase 5 — Frontend track (outline)

- Draft the frontend T map. Core candidates: TypeScript, the browser rendering pipeline, component architecture, state management, accessibility, testing. Breadth candidates: web performance (Core Web Vitals), caching and CDNs, API consumption (REST/GraphQL, error handling, optimistic UI), security (XSS, CSRF, CSP), build tooling, design systems, SSR/SSG vs SPA.
- Write module stubs with a scenario and stop-and-think questions only.
- Outline frontend-specific online-assessment content (framework tasks in the platform IDE, DOM and JavaScript output-prediction questions) and link it from `interviews/assessments/`.
- Athan reviews the outline before any full frontend content is written.

### Phase 6 — Interview layer and polish

- `interviews/` pages, cross-linked to the modules that each round draws on.
- Per-track self-assessment checklists with localStorage progress (wrapped in try/catch, works without storage).
- A "Learning path" view per track: suggested order for growth vs. interview prep.
- Accessibility and mobile pass, and a link checker in CI.

## 7. Known issues in the source material

Fix these during rewriting, and turn them into exercises:

| File | Issue |
|---|---|
| `05-databases/database-scaling-deep-dive.md` — `ShardRouter` | Uses `string.GetHashCode()`, which is randomised per process in modern .NET, so routing changes between restarts. Modulo hashing also makes adding shards expensive. Use a stable hash (for example, xxHash) and discuss consistent hashing. |
| `03-architecture/event-driven-architecture.md` — `TransactionService`, `EventSourcedAccountRepository` | Saves to the database and then publishes to the broker. A crash in between loses the event (dual-write). Use the transactional outbox pattern. |
| `06-cloud/messaging-events.md` — `EnqueueBatchAsync` | Reassigns a `using var` variable, which does not compile. |
| `02-system-design/idempotency.md` — `PaymentService` | Debits and credits across services with no transaction or compensation, and stores the idempotency result in a cache separately from the database write. Discuss failure windows. |
| `05-databases/database-scaling-deep-dive.md` | Too thin for the topic: it goes straight to sharding. Replace it with the module set in Phase 2. |

## 8. Open questions

- Hosting: GitHub Pages, or Netlify/Vercel with preview deploys?
- Will the site be public? This affects licensing and how much personal context is included.
- Should there be a fullstack track, or do cross-links between the two tracks cover it?
- Add other backend languages (Java, Go, Python) through code groups, and if so, when?
