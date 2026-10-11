import { expect, test, type Page } from '@playwright/test'
import { diagramPages } from './pages'

// Mermaid renders client-side, so a diagram that fails to parse or render is invisible
// to `docs:build`. These tests load every page that has a ```mermaid block and check
// that each one became an SVG, in both themes. They also save a screenshot of every
// diagram to test-results/diagrams/ for review (not compared against baselines).

const diagram = (page: Page) => page.locator('.mermaid svg')
const shotName = (url: string, theme: string, i: number) =>
  `test-results/diagrams/${url.replace(/^\/|\/$/g, '').replaceAll('/', '--') || 'home'}--${theme}-${i + 1}.png`

// Hide the sticky nav so it doesn't overlap tall diagrams in screenshots.
const hideChrome = '.VPNav, .VPLocalNav { visibility: hidden !important; }'

/**
 * Labels whose text overflows their box. Mermaid sizes each HTML label when it renders;
 * if it measures before the web font loads, multi-line labels get cut off.
 */
async function clippedLabels(page: Page, index: number): Promise<string[]> {
  return diagram(page)
    .nth(index)
    .evaluate((svg) =>
      Array.from(svg.querySelectorAll('foreignObject'))
        .filter((fo) => {
          const content = fo.firstElementChild
          if (!content || !content.textContent?.trim()) return false
          return content.getBoundingClientRect().height > fo.getBoundingClientRect().height + 1
        })
        .map((fo) => fo.textContent!.trim()),
    )
}

function collectErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`))
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(`console: ${m.text()}`)
  })
  return errors
}

test('there are diagram pages to check', () => {
  // Guards against the page discovery silently finding nothing.
  expect(diagramPages.length).toBeGreaterThan(0)
})

for (const { url, file, diagrams } of diagramPages) {
  test.describe(file, () => {
    test(`renders ${diagrams} diagram(s) in light and dark themes`, async ({ page }) => {
      const errors = collectErrors(page)
      await page.addInitScript(() => localStorage.setItem('vitepress-theme-appearance', 'light'))
      await page.goto(url)

      // Light theme: every block became an SVG with real content, and no Mermaid error.
      await expect(diagram(page)).toHaveCount(diagrams)
      await expect(page.getByText(/Syntax error in text|Parse error/)).toHaveCount(0)
      const lightStyles: string[] = []
      for (let i = 0; i < diagrams; i++) {
        const svg = diagram(page).nth(i)
        await expect(svg.locator('g.node, g.nodes > g').first()).toBeVisible()
        const box = await svg.boundingBox()
        expect(box?.width, 'diagram width').toBeGreaterThan(50)
        expect(box?.height, 'diagram height').toBeGreaterThan(20)
        expect(await clippedLabels(page, i), 'labels cut off (light)').toEqual([])
        lightStyles.push(await svg.locator('style').first().innerHTML())
        await svg.scrollIntoViewIfNeeded()
        await svg.screenshot({ path: shotName(url, 'light', i), style: hideChrome })
      }

      // Switch theme with the real toggle: diagrams must redraw with the dark theme.
      await page.locator('.VPNavBarAppearance .VPSwitchAppearance').click()
      await expect(page.locator('html')).toHaveClass(/dark/)
      await expect(diagram(page)).toHaveCount(diagrams)
      for (let i = 0; i < diagrams; i++) {
        const svg = diagram(page).nth(i)
        await expect
          .poll(() => svg.locator('style').first().innerHTML(), { message: 'diagram redrawn for dark theme' })
          .not.toBe(lightStyles[i])
        expect(await clippedLabels(page, i), 'labels cut off (dark)').toEqual([])
        await svg.scrollIntoViewIfNeeded()
        await svg.screenshot({ path: shotName(url, 'dark', i), style: hideChrome })
      }

      expect(errors).toEqual([])
    })
  })
}
