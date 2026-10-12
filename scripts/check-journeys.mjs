// Checks every written journey page against the templates in docs/_templates:
// frontmatter, section order, stop-and-think blocks, a spot-the-bug trap, interview
// follow-ups, and a "What breaks next" link. Draft pages are skipped.
//
//   node scripts/check-journeys.mjs
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const root = 'docs'
const stageSections = ['Where we are', 'Stop and think', 'The mental model', 'The escalation ladder',
  'Trade-offs at a glance', 'Traps', 'In the interview', 'Try it', 'What breaks next']
const capstoneSections = ['1. Clarify', '2. Estimate', '3. Design', '4. Deep dive',
  'The answer in two minutes', 'What to review']
const required = ['title', 'track', 'journey', 'stage', 'level', 'status', 'sources']

const problems = []
let checked = 0

for (const track of ['backend', 'frontend']) {
  const journeys = join(root, track, 'journeys')
  let dirs
  try { dirs = readdirSync(journeys, { withFileTypes: true }).filter(d => d.isDirectory()) } catch { continue }
  for (const dir of dirs) {
    for (const file of readdirSync(join(journeys, dir.name)).filter(f => f.endsWith('.md') && f !== 'index.md')) {
      const path = join(journeys, dir.name, file)
      const text = readFileSync(path, 'utf8')
      const fm = Object.fromEntries([...(text.match(/^---\n([\s\S]*?)\n---/)?.[1] ?? '').matchAll(/^(\w+):\s*(.*)$/gm)]
        .map(m => [m[1], m[2].trim()]))
      if (fm.status === 'draft') continue
      checked++
      const fail = message => problems.push(`${path}: ${message}`)

      for (const key of required) if (!(key in fm)) fail(`frontmatter has no ${key}`)
      if (fm.journey !== dir.name) fail(`journey is "${fm.journey}", folder is "${dir.name}"`)
      if (fm.track !== track) fail(`track is "${fm.track}", folder is "${track}"`)

      const headings = [...text.matchAll(/^## (.+)$/gm)].map(m => m[1].trim())
      const capstone = file === 'capstone.md'
      const expected = capstone ? capstoneSections : stageSections
      if (headings.join('|') !== expected.join('|')) fail(`sections are [${headings.join(', ')}]`)

      if (capstone) {
        if (fm.stage !== 'capstone') fail(`stage is "${fm.stage}", expected capstone`)
        if (!/::: think/.test(text)) fail('no stop-and-think block')
        for (const n of [1, 2, 3, 4, 5, 6, 7]) {
          if (!new RegExp(`\\]\\(\\./0${n}-`).test(text)) fail(`never links to stage ${n}`)
        }
        continue
      }

      const number = Number(file.match(/^(\d+)-/)?.[1])
      if (String(number) !== fm.stage) fail(`stage is "${fm.stage}", file name says ${number}`)
      if (!new RegExp(`^# ${number}\\. `, 'm').test(text)) fail(`heading isn't numbered ${number}`)

      const section = name => text.split(`## ${name}`)[1]?.split(/^## /m)[0] ?? ''
      const thinks = (section('Stop and think').match(/^::: think /gm) ?? []).length
      if (thinks < 2) fail(`${thinks} stop-and-think blocks, expected at least 2`)
      if (!/^:{3,} trap/m.test(section('Traps'))) fail('no spot-the-bug trap block')
      const followUps = (section('In the interview').match(/^::: details Follow-up/gm) ?? []).length
      if (followUps < 3) fail(`${followUps} interview follow-ups, expected at least 3`)
      if (!/\]\(\.\/(0\d-[\w-]+|capstone)\)/.test(section('What breaks next'))) fail('"What breaks next" links nowhere')
    }
  }
}

if (problems.length) {
  console.error(problems.join('\n'))
  console.error(`\n${problems.length} problem(s) in ${checked} page(s).`)
  process.exit(1)
}
console.log(`${checked} journey page(s) match the templates.`)
