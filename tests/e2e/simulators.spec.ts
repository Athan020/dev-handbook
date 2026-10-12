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

test.describe('read replica simulator', () => {
  const url = '/backend/journeys/your-api-gets-popular/06-read-replicas'

  test('reading everything from the replica hides customers’ own writes', async ({ page }) => {
    const sim = await runScenario(page, url, 'Replica for everything', 10)
    expect(await statNumber(sim, 'primary')).toBe(0)
    expect(await statNumber(sim, 'stale')).toBeGreaterThan(20)
  })

  test('a sticky window protects read-backs with little primary load', async ({ page }) => {
    const sim = await runScenario(page, url, 'Sticky for 5 s', 10)
    expect(await statNumber(sim, 'stale')).toBe(0)
    expect(await statNumber(sim, 'primary')).toBeLessThan(15)
  })

  test('a consistency token stays correct through lag spikes', async ({ page }) => {
    const sim = await runScenario(page, url, 'Token, with lag spikes', 20)
    expect(await statNumber(sim, 'stale')).toBe(0)
    expect(await statNumber(sim, 'primary')).toBeLessThan(15)
  })
})

test.describe('sharding simulator', () => {
  const url = '/backend/journeys/your-api-gets-popular/07-partitioning-and-sharding'

  async function addShardWith(page: Page, method: 'modulo' | 'ring') {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    await sim.getByLabel('Choose a shard by').selectOption(method)
    await sim.getByRole('button', { name: 'Add a shard' }).click()
    return sim
  }

  test('modulo moves most accounts when a shard is added', async ({ page }) => {
    const sim = await addShardWith(page, 'modulo')
    expect(await statNumber(sim, 'moved')).toBeGreaterThan(70)
  })

  test('a ring moves about a fair share', async ({ page }) => {
    const sim = await addShardWith(page, 'ring')
    const moved = await statNumber(sim, 'moved')
    expect(moved).toBeGreaterThan(14)
    expect(moved).toBeLessThan(28)
  })

  test('one virtual node per shard makes the load very uneven', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    await sim.getByLabel('Virtual nodes per shard').fill('1')
    expect(parseFloat(await sim.locator('[data-stat="max"]').innerText())).toBeGreaterThan(1.5)
  })

  test('realistic load makes the busiest shard busier', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    const even = parseFloat(await sim.locator('[data-stat="max"]').innerText())
    await sim.getByLabel('Load per account').check()
    expect(parseFloat(await sim.locator('[data-stat="max"]').innerText())).toBeGreaterThan(even)
  })
})

const cell = async (sim: Locator, strategy: string, column: string) =>
  Number(await sim.locator(`tr[data-strategy="${strategy}"] td[data-col="${column}"]`).innerText())

test.describe('duplicate delivery simulator', () => {
  const url = '/backend/journeys/work-outside-the-request/03-delivered-twice'

  test('only acting and recording together gives exactly-once effects', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    expect(await cell(sim, 'none', 'duplicated')).toBeGreaterThan(0)
    expect(await cell(sim, 'markFirst', 'lost')).toBeGreaterThan(0)
    expect(await cell(sim, 'effectFirst', 'duplicated')).toBeGreaterThan(0)
    expect(await cell(sim, 'effectFirst', 'duplicated')).toBeLessThan(await cell(sim, 'none', 'duplicated'))
    expect(await cell(sim, 'together', 'once')).toBe(1000)
  })

  test('with no crashes, every strategy is correct', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    await sim.getByLabel(/Chance of crashing/).fill('0')
    for (const s of ['none', 'markFirst', 'effectFirst', 'together']) expect(await cell(sim, s, 'once')).toBe(1000)
  })
})

test.describe('outbox simulator', () => {
  const url = '/backend/journeys/work-outside-the-request/04-dual-write-and-outbox'

  test('each dual-write order fails its own way; outbox plus idempotent consumer is correct', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    expect(await cell(sim, 'afterCommit', 'lost')).toBeGreaterThan(0)
    expect(await cell(sim, 'beforeCommit', 'ghost')).toBeGreaterThan(0)
    expect(await cell(sim, 'outbox', 'lost')).toBe(0)
    expect(await cell(sim, 'outbox', 'duplicated')).toBeGreaterThan(0)
    expect(await cell(sim, 'outboxIdempotent', 'correct')).toBe(1000)
  })
})

test.describe('backlog simulator', () => {
  const url = '/backend/journeys/work-outside-the-request/07-backpressure'
  const stat = async (sim: Locator, key: string) => (await sim.locator(`[data-stat="${key}"]`).innerText()).trim()
  const peak = async (sim: Locator) => Number((await stat(sim, 'peak')).replace(/\D/g, ''))

  async function preset(page: Page, label: string) {
    await page.goto(url)
    const sim = page.locator('.simulator').first()
    await sim.getByRole('button', { name: label, exact: true }).click()
    return sim
  }

  test('an ordinary day keeps up', async ({ page }) => {
    expect(await peak(await preset(page, 'An ordinary day'))).toBeLessThan(10)
  })

  test('payroll builds a backlog that more capacity shrinks, up to the provider limit', async ({ page }) => {
    const one = await peak(await preset(page, 'Payroll, one worker'))
    const atLimit = await peak(await preset(page, 'Payroll, at the provider limit'))
    const beyond = await peak(await preset(page, 'More workers than the provider allows'))
    expect(one).toBeGreaterThan(10_000)
    expect(atLimit).toBeLessThan(one)
    expect(beyond).toBe(atLimit) // the provider caps throughput
  })

  test('a bounded queue rejects work instead of growing past its limit', async ({ page }) => {
    const sim = await preset(page, 'Bounded queue')
    expect(await peak(sim)).toBeLessThanOrEqual(10_000)
    expect(Number((await stat(sim, 'rejected')).replace(/\D/g, ''))).toBeGreaterThan(0)
  })

  test('hovering the chart reports the depth and wait at that moment', async ({ page }) => {
    const sim = await preset(page, 'Payroll, one worker')
    const chart = sim.locator('.chart svg')
    await chart.scrollIntoViewIfNeeded()
    const box = (await chart.boundingBox())!
    await page.mouse.move(box.x + box.width * 0.45, box.y + box.height / 2)
    await expect(sim.locator('.tooltip')).toContainText(/waiting; the email sent now waited/)
  })
})

test.describe('interleaving simulator', () => {
  const url = '/backend/journeys/the-money-must-add-up/01-check-then-act'

  async function run(page: Page, scenario: string, level: string, action: 'interleave' | 'serial') {
    await page.goto(url)
    const sim = page.locator('.interleaving')
    await sim.locator(`[data-scenario="${scenario}"]`).click()
    await sim.locator(`[data-level="${level}"]`).click()
    await sim.locator(`[data-action="${action}"]`).click()
    return sim
  }
  const verdict = (sim: Locator) => sim.locator('[data-stat="verdict"]')
  const balance = async (sim: Locator) => Number(await sim.locator('[data-stat="balance"]').innerText())

  test('check then insert overdraws at read committed and repeatable read (write skew)', async ({ page }) => {
    for (const level of ['rc', 'rr']) {
      const sim = await run(page, 'check-insert', level, 'interleave')
      await expect(verdict(sim)).toContainText('Overdrawn')
      expect(await balance(sim)).toBe(-20)
    }
  })

  test('serializable aborts one transfer instead of overdrawing', async ({ page }) => {
    const sim = await run(page, 'check-insert', 'ser', 'interleave')
    await expect(verdict(sim)).toContainText('Correct')
    expect(await balance(sim)).toBe(40)
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'aborted')
  })

  test('the same code is correct when the transactions do not overlap', async ({ page }) => {
    const sim = await run(page, 'check-insert', 'rc', 'serial')
    await expect(verdict(sim)).toContainText('Correct')
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'rolledback')
  })

  test('read then write loses an update at read committed, and fails the second writer at repeatable read', async ({ page }) => {
    let sim = await run(page, 'read-write', 'rc', 'interleave')
    await expect(verdict(sim)).toContainText('Lost update')
    sim = await run(page, 'read-write', 'rr', 'interleave')
    await expect(verdict(sim)).toContainText('Correct')
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'aborted')
  })

  test('a conditional update is correct even at read committed', async ({ page }) => {
    const sim = await run(page, 'conditional', 'rc', 'interleave')
    await expect(verdict(sim)).toContainText('Correct')
    expect(await balance(sim)).toBe(40)
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'rolledback')
  })

  test('stepping by hand: a blocked transaction cannot step until the lock is released', async ({ page }) => {
    await page.goto(url)
    const sim = page.locator('.interleaving')
    await sim.locator('[data-scenario="conditional"]').click()
    const step = (t: string) => sim.locator(`[data-txn="${t}"] button.step`)
    await step('T1').click() // BEGIN
    await step('T2').click() // BEGIN
    await step('T1').click() // UPDATE, takes the lock
    await step('T2').click() // UPDATE, waits
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'blocked')
    await expect(step('T2')).toBeDisabled()
    await step('T1').click() // one row updated
    await step('T1').click() // COMMIT releases the lock; T2 resumes and finds 40
    await expect(sim.locator('[data-txn="T2"]')).toHaveAttribute('data-status', 'active')
    await step('T2').click() // no row updated: ROLLBACK
    await expect(verdict(sim)).toContainText('Correct')
  })
})
