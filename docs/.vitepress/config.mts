import { defineConfig, type DefaultTheme } from 'vitepress'
import container from 'markdown-it-container'

// Named containers on top of VitePress's built-in tip/info/warning/danger/details.
// `::: interview` and `::: trap` take an optional custom title, like the built-ins.
const customContainers: Record<string, string> = {
  interview: 'In the interview',
  trap: 'Spot the bug',
}

// Short, stable hash for storage keys (djb2).
function hash(text: string): string {
  let h = 5381
  for (let i = 0; i < text.length; i++) h = ((h << 5) + h + text.charCodeAt(i)) | 0
  return (h >>> 0).toString(36)
}

// Backend journeys: stages in order, then the capstone. [file, sidebar text]
const backendJourneys: { slug: string; text: string; stages: [string, string][] }[] = [
  {
    slug: 'your-api-gets-popular',
    text: 'B1: Your API gets popular',
    stages: [
      ['01-measure-first', '1. Measure first'],
      ['02-indexes-and-query-shape', '2. Indexes and query shape'],
      ['03-what-is-the-orm-doing', '3. What is the ORM doing?'],
      ['04-too-many-connections', '4. Too many connections'],
      ['05-cache-then-invalidate', '5. Cache it, then invalidate it'],
      ['06-read-replicas', '6. Reads outgrow one box'],
      ['07-partitioning-and-sharding', '7. Writes outgrow one box'],
    ],
  },
  {
    slug: 'work-outside-the-request',
    text: "B2: Work that shouldn't happen in the request",
    stages: [
      ['01-get-it-out-of-the-request', '1. Get it out of the request'],
      ['02-queues-topics-or-streams', '2. Queues, topics or streams'],
      ['03-delivered-twice', '3. Delivered twice'],
      ['04-dual-write-and-outbox', '4. The event that never got sent'],
      ['05-ordering', '5. Order matters, sometimes'],
      ['06-poison-messages', '6. Poison messages'],
      ['07-backpressure', '7. The queue keeps growing'],
    ],
  },
]

const backendSidebar: DefaultTheme.SidebarItem[] = [
  { text: 'Backend track', link: '/backend/' },
  { text: 'Core self-check', link: '/backend/core-self-check' },
  {
    text: 'Journeys',
    items: backendJourneys.map(({ slug, text, stages }) => {
      const base = `/backend/journeys/${slug}/`
      return {
        text,
        link: base,
        collapsed: true,
        items: [
          ...stages.map(([file, stageText]) => ({ text: stageText, link: base + file })),
          { text: '★ Capstone', link: base + 'capstone' },
        ],
      }
    }),
  },
]

const frontendSidebar: DefaultTheme.SidebarItem[] = [
  { text: 'Frontend track', link: '/frontend/' },
  { text: 'Core self-check', link: '/frontend/core-self-check' },
]

const interviewsSidebar: DefaultTheme.SidebarItem[] = [
  {
    text: 'Interviews',
    items: [
      { text: 'Overview', link: '/interviews/' },
      { text: 'Interview stages', link: '/interviews/stages' },
      { text: 'System design framework', link: '/interviews/system-design-framework' },
      { text: 'Coding patterns', link: '/interviews/coding-patterns' },
      { text: 'Behavioural interviews', link: '/interviews/behavioural' },
    ],
  },
  {
    text: 'Online assessments',
    collapsed: false,
    items: [
      { text: 'Overview', link: '/interviews/assessments/' },
      { text: 'Before the test', link: '/interviews/assessments/before-the-test' },
      { text: 'Test-day strategy', link: '/interviews/assessments/test-day-strategy' },
      { text: 'Reading constraints', link: '/interviews/assessments/reading-constraints' },
      {
        text: 'Question types',
        link: '/interviews/assessments/question-types/',
        collapsed: true,
        items: [
          { text: 'Coding', link: '/interviews/assessments/question-types/coding' },
          { text: 'SQL', link: '/interviews/assessments/question-types/sql' },
          { text: 'Project tasks', link: '/interviews/assessments/question-types/projects' },
          { text: 'Code review and debugging', link: '/interviews/assessments/question-types/code-review-and-debugging' },
          { text: 'Multiple choice', link: '/interviews/assessments/question-types/multiple-choice' },
        ],
      },
      { text: 'C# toolkit', link: '/interviews/assessments/csharp-toolkit' },
      { text: 'Practice sets', link: '/interviews/assessments/practice/' },
    ],
  },
]

const guideSidebar: DefaultTheme.SidebarItem[] = [
  {
    text: 'Guide',
    items: [
      { text: 'T-shaped skills', link: '/guide/t-shaped-skills' },
      { text: 'How journeys work', link: '/guide/how-journeys-work' },
      { text: 'How a stage page works', link: '/guide/module-format' },
    ],
  },
]

export default defineConfig({
  title: 'The Engineering Handbook',
  description: 'Build T-shaped software engineering skills, organised by orientation.',
  lang: 'en-GB',
  // CI sets VITEPRESS_BASE to /<repo-name>/ for GitHub Pages project sites.
  base: process.env.VITEPRESS_BASE ?? '/',
  cleanUrls: true,
  // _source/ and samples/ live outside docs/, so these entries are a safety net.
  srcExclude: ['**/_source/**', '**/samples/**', '_templates/**'],

  vite: {
    // PGlite loads its WebAssembly files relative to its own module, which pre-bundling breaks.
    optimizeDeps: { exclude: ['@electric-sql/pglite'] },
  },

  themeConfig: {
    nav: [
      { text: 'Guide', link: '/guide/t-shaped-skills', activeMatch: '^/guide/' },
      { text: 'Backend', link: '/backend/', activeMatch: '^/backend/' },
      { text: 'Frontend', link: '/frontend/', activeMatch: '^/frontend/' },
      { text: 'Interviews', link: '/interviews/', activeMatch: '^/interviews/' },
    ],
    sidebar: {
      '/guide/': guideSidebar,
      '/backend/': backendSidebar,
      '/frontend/': frontendSidebar,
      '/interviews/': interviewsSidebar,
    },
    search: { provider: 'local' },
    outline: { level: [2, 3] },
  },

  markdown: {
    config(md) {
      // ```mermaid blocks become <MermaidDiagram>, and ```sql playground blocks become
      // <SqlPlayground>. Both components are registered in theme/index.ts.
      const fence = md.renderer.rules.fence!
      md.renderer.rules.fence = (tokens, idx, options, env, self) => {
        const token = tokens[idx]
        const info = token.info.trim()
        const code = encodeURIComponent(token.content)
        if (info === 'mermaid') return `<MermaidDiagram code="${code}" />\n`
        if (info === 'sql playground') return `<SqlPlayground code="${code}" />\n`
        return fence(tokens, idx, options, env, self)
      }

      // `::: think Question?` becomes <StopAndThink>; the block body is the model answer.
      // The storage key hashes the page path and question, so a reader's saved answer
      // survives rebuilds but resets if the question is reworded.
      md.use(container, 'think', {
        render(tokens: any[], idx: number, _options: unknown, env: { relativePath?: string }) {
          const token = tokens[idx]
          if (token.nesting !== 1) return '</StopAndThink>\n'
          const question = token.info.trim().slice('think'.length).trim()
          if (!question) throw new Error(`::: think needs a question in ${env.relativePath}`)
          const key = hash(`${env.relativePath}:${question}`)
          return `<StopAndThink storage-key="${key}"><template #question><p>${md.renderInline(question, env)}</p></template>\n`
        },
      })

      for (const [name, defaultTitle] of Object.entries(customContainers)) {
        md.use(container, name, {
          render(tokens: any[], idx: number) {
            const token = tokens[idx]
            if (token.nesting !== 1) return '</div>\n'
            const title = token.info.trim().slice(name.length).trim() || defaultTitle
            return `<div class="${name} custom-block"><p class="custom-block-title">${md.utils.escapeHtml(title)}</p>\n`
          },
        })
      }
    },
  },
})
