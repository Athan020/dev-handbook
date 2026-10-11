# The Engineering Handbook

A VitePress site for building T-shaped software engineering skills, organised by orientation (frontend / backend). **`PLAN.md` is the source of truth** for scope, structure, phases and decisions. Read it before starting any work.

## Commands

- `npm run docs:dev`: local dev server
- `npm run docs:build`: production build. It must pass with no dead links before a phase is complete.
- `npm run docs:preview`: preview the build
- `node scripts/migrate-obsidian.mjs`: migration script (Phase 1)
- `dotnet build samples/`: compile the runnable code samples (Phase 2 onwards)

## Layout

- `docs/`: site content (VitePress source root)
- `docs/.vitepress/`: config, theme, custom containers
- `_source/backend-engineer-kb/`: original Obsidian notes. **Read-only. Never edit, move or delete anything here.**
- `samples/`: .NET solution and Docker Compose labs. Snippets are imported into pages with `<<< @/samples/...`.
- `scripts/`: tooling (migration, link checks)

## How to work

- Work **one phase at a time**, in the order given in `PLAN.md`. Before starting a phase, propose a short plan. At the end of a phase, stop, summarise what changed, list anything uncertain, and wait for approval.
- Update the **Status** line at the top of `PLAN.md` when a phase starts or finishes.
- Use scripts for mechanical changes, such as syntax conversion across many files. Do not hand-edit dozens of files.
- If the plan and the source material disagree, or a mapping is ambiguous, ask instead of guessing.
- Commit at logical checkpoints with clear messages. Do not push unless asked.

## Writing rules for modules

- Follow the module template in `PLAN.md` §5. `_source/backend-engineer-kb/09-system-design-prep/08-troubleshoot-slow-reads.md` is the tone reference.
- Lead with a scenario. Put answers to stop-and-think questions inside `::: details` blocks.
- Present options as an escalation ladder, simplest and cheapest first. Always say what each option breaks and when not to use it.
- Explain concepts in language-neutral terms. Use C# for backend code examples.
- Code must compile. Wrong code appears only inside a `::: trap` block, explicitly framed as "spot the bug".
- Do not hard-code vendor limits, quotas or prices. Link to official docs.
- Banking is a recurring example domain. Do not include company-specific or job-application content.
- Write in plain, direct prose. Avoid filler, marketing tone and emoji headings.

## Frontmatter

Every content page has `title`, `track`, `layer`, `level`, `status` (`draft | migrated | rewritten | reviewed`), and `prerequisites` where relevant.
