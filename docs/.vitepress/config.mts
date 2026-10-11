import { defineConfig, type DefaultTheme } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import container from 'markdown-it-container'

// Named containers on top of VitePress's built-in tip/info/warning/danger/details.
// `::: interview` and `::: trap` take an optional custom title, like the built-ins.
const customContainers: Record<string, string> = {
  interview: 'In the interview',
  trap: 'Spot the bug',
}

const backendSidebar: DefaultTheme.SidebarItem[] = [
  { text: 'Backend track', link: '/backend/' },
  {
    text: 'Core',
    collapsed: false,
    items: [
      { text: 'API design', link: '/backend/core/api-design/' },
      { text: 'Data modelling and SQL', link: '/backend/core/data-modelling-and-sql/' },
      { text: 'Authentication and authorisation', link: '/backend/core/auth/' },
      { text: 'Code design', link: '/backend/core/code-design/' },
      { text: '.NET in depth', link: '/backend/core/dotnet/' },
      { text: 'Delivery', link: '/backend/core/delivery/' },
    ],
  },
  {
    text: 'Breadth',
    collapsed: false,
    items: [
      { text: 'Databases at scale', link: '/backend/breadth/databases-at-scale/' },
      { text: 'Messaging', link: '/backend/breadth/messaging/' },
      { text: 'Caching', link: '/backend/breadth/caching/' },
      { text: 'Distributed systems', link: '/backend/breadth/distributed-systems/' },
      { text: 'Architecture', link: '/backend/breadth/architecture/' },
      { text: 'Cloud and infrastructure', link: '/backend/breadth/cloud-and-infra/' },
      { text: 'Observability', link: '/backend/breadth/observability/' },
      { text: 'Networking', link: '/backend/breadth/networking/' },
      { text: 'Security', link: '/backend/breadth/security/' },
    ],
  },
  {
    text: 'Scenarios',
    collapsed: false,
    items: [{ text: 'Overview', link: '/backend/scenarios/' }],
  },
]

const frontendSidebar: DefaultTheme.SidebarItem[] = [
  { text: 'Frontend track', link: '/frontend/' },
  { text: 'Core', link: '/frontend/core/' },
  { text: 'Breadth', link: '/frontend/breadth/' },
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
      { text: 'How to use this handbook', link: '/guide/how-to-use' },
      { text: 'How a module works', link: '/guide/module-format' },
    ],
  },
]

export default withMermaid(
  defineConfig({
    title: 'The Engineering Handbook',
    description: 'Build T-shaped software engineering skills, organised by orientation.',
    lang: 'en-GB',
    // CI sets VITEPRESS_BASE to /<repo-name>/ for GitHub Pages project sites.
    base: process.env.VITEPRESS_BASE ?? '/',
    cleanUrls: true,
    // _source/ and samples/ live outside docs/, so these entries are a safety net.
    srcExclude: ['**/_source/**', '**/samples/**', '_templates/**'],

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

    mermaid: {
      // The plugin defaults to 'loose'; we don't need click handlers or HTML labels.
      securityLevel: 'strict',
    },

    markdown: {
      config(md) {
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
  }),
)
