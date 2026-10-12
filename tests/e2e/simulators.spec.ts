import { expect, test, type Locator, type Page } from '@playwright/test'

// The simulators are deterministic (seeded) and run in real time, so each test lets a
// scenario run for a few simulated seconds and checks the behaviour the page describes.
test.setTimeout(90_000)

const statNumber = async (sim: Locator, key: string) => {
  const text = (await sim.locator(`[data-stat="${key}"]`).innerText()).trim()
  const value = parseFloat(text.replace(/[^\d.]/g, ''))
  return text.endsWith(' s') ? value * 1000 : value
}

async function runScenario(page: Page, url: string, label: string, seconds: number) {
  await page.goto(url)
  const sim = page.locator('.simulator').first()
  await sim.getByRole('button', { name: label, exact: true }).click()
  await expect(sim.locator('.note')).toContainText(`${seconds} s`, { timeout: (seconds + 15) * 1000 })
  return sim
}

test.describe('connection pool simulator', () => {
  const url = '/backend/journeys/your-api-gets-popular/04-too-many-connections'

  test('comfortable load: no queue, no timeouts, latency near the query time', async ({ page }) => {
    const sim = await runScenario(page, url, 'Comfortable', 8)
    expect(await statNumber(sim, 'failed')).toBe(0)
    expect(await statNumber(sim, 'queued')).toBeLessThan(10)
    expect(await statNumber(sim, 'p50')).toBeLessThan(100)
  })

  test('overload: the queue grows and latency climbs towards the timeout', async ({ page }) => {
    const sim = await runScenario(page, url, 'Overloaded', 8)
    expect(await statNumber(sim, 'queued')).toBeGreaterThan(100)
    expect(await statNumber(sim, 'p50')).toBeGreaterThan(1000)
  })

  test('a short timeout fails fast instead of queuing', async ({ page }) => {
    const sim = await runScenario(page, url, 'Overloaded, short timeout', 8)
    expect(await statNumber(sim, 'failed')).toBeGreaterThan(0)
    expect(await statNumber(sim, 'p99')).toBeLessThan(1600)
  })

  test('a bigger pool does not raise throughput beyond the database cores', async ({ page }) => {
    const small = await runScenario(page, url, 'Overloaded', 8)
    const smallDone = await statNumber(small, 'completed')
    const big = await runScenario(page, url, 'Overloaded, pool of 50', 8)
    const bigDone = await statNumber(big, 'completed')
    expect(Math.abs(bigDone - smallDone)).toBeLessThan(20)
  })

  test('sliders are labelled and change the setup', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    const pool = sim.getByLabel(/Pool size/)
    await pool.fill('20')
    await expect(sim.locator('.pool .slot')).toHaveCount(20)
    await expect(sim.getByRole('button', { name: 'Comfortable', exact: true })).toHaveAttribute('aria-pressed', 'false')
  })
})

test.describe('cache stampede simulator', () => {
  const url = '/backend/journeys/your-api-gets-popular/05-cache-then-invalidate'
  // The first expiry happens at 5 s; by 9 s every strategy has been through one.

  test('every miss recomputing piles up duplicate queries', async ({ page }) => {
    const sim = await runScenario(page, url, 'Every miss recomputes', 9)
    expect(await statNumber(sim, 'peak')).toBeGreaterThan(50)
    expect(await statNumber(sim, 'slowest')).toBeGreaterThan(2000)
  })

  test('coalescing runs one recompute at a time', async ({ page }) => {
    const sim = await runScenario(page, url, 'One recompute, others wait', 9)
    expect(await statNumber(sim, 'peak')).toBe(1)
    expect(await statNumber(sim, 'slowest')).toBeLessThan(1000)
  })

  test('serving stale keeps latency low but serves data older than the TTL', async ({ page }) => {
    const sim = await runScenario(page, url, 'Serve stale while refreshing', 9)
    expect(await statNumber(sim, 'slowest')).toBeLessThan(50)
    expect(await statNumber(sim, 'oldest')).toBeGreaterThan(5000)
  })
})
