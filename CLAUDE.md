# The Engineering Handbook

A VitePress site for building T-shaped software engineering skills, organised by orientation (frontend / backend) and structured as **journeys**: each one starts from something the reader already owns, applies pressure, and moves outward one stage at a time.

**`PLAN.md` (v2) is the source of truth** for scope, structure, phases and decisions. Read it before starting any work. If you remember instructions from the v1 plan (topic folders, an Obsidian migration phase, a mapping table), they are superseded. See `PLAN.md` §9.

## Commands

- `npm run docs:dev`: local dev server
- `npm run docs:build`: production build. It must pass with no dead links before a phase is complete.
- `npm run docs:preview`: preview the build
- `npm run test:e2e`: build, then run the Playwright browser tests (diagrams render in both themes, navigation, every page loads cleanly). Diagram screenshots land in `test-results/diagrams/` for review. First run: `npx playwright install chromium`.
- `docker compose up -d` (in `samples/`): start Postgres and, from Phase 2, RabbitMQ for the labs. Add `--profile replica` for the read replica (B1 stage 6).
- `dotnet run --project samples/src/Bank.Api -- seed`: create the schema and seed 10M transactions (one to three minutes; `docker compose down -v` to start over)
- `dotnet build samples/Samples.sln` and `dotnet test --solution samples/Samples.sln`: compile and test the samples (the tests need Docker running)
- `dotnet run --project samples/src/Bank.Api -- schema`: regenerate `Data/Seed/schema.sql` (the SQL playground's schema) after changing the model; a test fails if it's stale

## Layout

- `docs/`: site content (VitePress source root)
- `docs/.vitepress/`: config, theme, custom containers
- `docs/<track>/journeys/<journey>/`: one folder per journey, with `index.md` plus numbered stage pages
- `_source/backend-engineer-kb/`: Athan's earlier notes. **Read-only. Never edit, move or delete anything here.** It is a quarry to mine per stage, not a structure to migrate.
- `samples/`: the .NET sample app, labs and assessment harnesses. Snippets are imported into pages with `<<< @/../samples/...` (`@` resolves to `docs/`, so `samples/` is one level up).

## How to work

- Work **one phase at a time**, in the order given in `PLAN.md` §10. Before starting a phase, propose a short plan. At the end of a phase, stop, summarise what changed, list anything uncertain, and wait for approval.
- Update the **Status** line at the top of `PLAN.md` when a phase starts or finishes.
- Before writing a journey that only has a draft outline (B3–B9, F1–F6), propose its full stage table and capstone and get approval.
- Journeys define scope. Do not add pages just because a source note exists, and do not skip a needed topic just because no note exists.
- If the plan is ambiguous or seems wrong, ask instead of guessing.
- Commit at logical checkpoints with clear messages. Do not push unless asked.

## Using `_source/`

For each stage: search `_source/` for relevant notes, reuse only what is correct and useful (rewritten in the stage's voice, never pasted wholesale), check code against `PLAN.md` §8, and list consulted notes in the `sources:` frontmatter.

## Writing rules

- Follow the stage template in `PLAN.md` §7. `_source/backend-engineer-kb/09-system-design-prep/08-troubleshoot-slow-reads.md` is the tone reference.
- Open each stage with the trigger from the previous stage, and close it with what breaks next.
- Write stop-and-think questions as `::: think Question?` blocks with the model answer as the body; the reader commits to an answer before it's revealed. Start new pages from `docs/_templates/stage.md` or `capstone.md`.
- For SQL the reader should run, use a ` ```sql playground ` block: it runs against real Postgres in the browser, seeded like the lab database but smaller (5,000 accounts, 250,000 transactions). Playgrounds on one page share a database, so an index created in one is used by the next.
- Present options as an escalation ladder, simplest and cheapest first. Always say what each option breaks and when not to use it.
- Explain concepts in language-neutral terms. Use C# for backend code.
- Code must compile. Wrong code appears only inside a `::: trap` block, explicitly framed as "spot the bug".
- Do not hard-code vendor limits, quotas or prices. Link to official docs.
- Banking is the recurring example domain. Do not include company-specific or job-application content.
- For fundamentals and algorithm drills, write a short self-check and link out instead of re-teaching what good resources already cover.
- Write in plain, direct prose. Avoid filler, marketing tone and emoji headings.

## Frontmatter

Every stage page has `title`, `track`, `journey`, `stage`, `level`, `status` (`draft | written | reviewed`), and `sources` when `_source/` notes were consulted.
