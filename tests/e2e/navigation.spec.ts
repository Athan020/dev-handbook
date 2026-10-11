import { expect, test } from '@playwright/test'
import { pages } from './pages'

test('landing page hero routes to both tracks', async ({ page }) => {
  for (const [text, url, heading] of [
    ['Backend', '/backend/', 'Backend track'],
    ['Frontend', '/frontend/', 'Frontend track'],
  ] as const) {
    await page.goto('/')
    await page.locator('.VPHero').getByRole('link', { name: text, exact: true }).click()
    await expect(page).toHaveURL(url)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(heading)
  }
})

test('backend sidebar lists the journeys with stages in order', async ({ page }) => {
  await page.goto('/backend/journeys/your-api-gets-popular/')
  const sidebar = page.locator('.VPSidebar')
  await expect(sidebar.getByRole('link', { name: 'B1: Your API gets popular' })).toBeVisible()
  await expect(sidebar.getByRole('link', { name: "B2: Work that shouldn't happen in the request" })).toBeVisible()

  // B1 is the current journey, so its group is expanded.
  const b1Stages = sidebar.locator('a[href^="/backend/journeys/your-api-gets-popular/"]')
  const texts = await b1Stages.allInnerTexts()
  expect(texts.slice(1)).toEqual([
    '1. Measure first',
    '2. Indexes and query shape',
    '3. What is the ORM doing?',
    '4. Too many connections',
    '5. Cache it, then invalidate it',
    '6. Reads outgrow one box',
    '7. Writes outgrow one box',
    '★ Capstone',
  ])
})

// Every page loads, has an H1, and logs no errors.
for (const { url, file } of pages) {
  test(`page loads cleanly: ${file}`, async ({ page }) => {
    const errors: string[] = []
    page.on('pageerror', (e) => errors.push(e.message))
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()))
    const response = await page.goto(url)
    expect(response?.status()).toBe(200)
    if (file !== 'index.md') await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
    await expect(page.locator('.NotFound')).toHaveCount(0)
    expect(errors).toEqual([])
  })
}
