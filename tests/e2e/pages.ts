import { globSync, readFileSync } from 'node:fs'

export interface DocPage {
  /** Path relative to docs/, e.g. `backend/index.md` */
  file: string
  /** URL path with clean URLs, e.g. `/backend/` */
  url: string
  /** Number of ```mermaid blocks in the source */
  diagrams: number
}

const ignored = ['.vitepress/**', '_templates/**']

/** Every content page under docs/, discovered from source so new pages are tested automatically. */
export const pages: DocPage[] = globSync('**/*.md', { cwd: 'docs', exclude: ignored })
  .sort()
  .map((file) => ({
    file,
    url: '/' + file.replace(/(^|\/)index\.md$/, '$1').replace(/\.md$/, ''),
    diagrams: (readFileSync(`docs/${file}`, 'utf8').match(/^```mermaid\s*$/gm) ?? []).length,
  }))

export const diagramPages = pages.filter((p) => p.diagrams > 0)
