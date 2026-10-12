import { expect, test, type Page } from '@playwright/test'

// The live example on the module-format guide page. Its starting query is an
// EXPLAIN ANALYZE of one account's newest transactions.
const url = '/guide/module-format'

// Starting Postgres in the browser and generating the data takes a few seconds,
// longer on a loaded CI machine.
test.setTimeout(120_000)
const STARTUP = { timeout: 90_000 }

const playground = (page: Page) => page.locator('.sql-playground').first()
const editor = (page: Page) => playground(page).getByLabel('SQL to run')
const runButton = (page: Page) => playground(page).getByRole('button', { name: /Run|Starting|Running/ })
const output = (page: Page) => playground(page).locator('.results')

async function runSql(page: Page, sql: string) {
  await editor(page).fill(sql)
  await runButton(page).click()
  await expect(runButton(page)).toHaveText('Run', STARTUP)
}

test('loads nothing until the reader runs a query, then shows the real plan', async ({ page }) => {
  const pgliteRequests: string[] = []
  page.on('request', (r) => /pglite|initdb/.test(r.url()) && pgliteRequests.push(r.url()))

  await page.goto(url)
  await expect(editor(page)).toHaveValue(/EXPLAIN ANALYZE/)
  await page.waitForTimeout(1000)
  expect(pgliteRequests, 'no database download before the first run').toEqual([])

  const started = Date.now()
  await runButton(page).click()
  await expect(playground(page).getByRole('status')).toContainText('Starting Postgres')
  await expect(output(page).locator('.plan')).toContainText('Seq Scan on transactions', STARTUP)
  test.info().annotations.push({ type: 'first run (start + query)', description: `${Date.now() - started} ms` })
  await expect(output(page)).toContainText(/Ran in [\d.]+ ms/)
})

test('an index created in the playground changes the plan, and reset removes it', async ({ page }) => {
  await page.goto(url)
  const query = `EXPLAIN SELECT * FROM transactions WHERE account_id = 42 ORDER BY created_at DESC LIMIT 20;`

  await runSql(page, `CREATE INDEX ON transactions (account_id, created_at DESC);\n${query}`)
  await expect(output(page).locator('.plan')).toContainText('Index Scan using transactions_account_id_created_at_idx')

  await playground(page).getByRole('button', { name: 'Reset database' }).click()
  await runSql(page, query)
  await expect(output(page).locator('.plan')).toContainText('Seq Scan on transactions')
})

test('shows query results as a table, capped at 50 rows', async ({ page }) => {
  await page.goto(url)

  await runSql(page, 'SELECT count(*) AS transactions FROM transactions;')
  await expect(output(page).locator('td').first()).toHaveText('250000')

  await runSql(page, 'SELECT id, holder_name FROM accounts ORDER BY id;')
  await expect(output(page).locator('tbody tr')).toHaveCount(50)
  await expect(output(page)).toContainText('5,000 rows, showing the first 50')
})

test('the data has the same skew as the lab database', async ({ page }) => {
  await page.goto(url)
  await runSql(
    page,
    `SELECT round(100.0 * count(*) FILTER (WHERE account_id = 1) / count(*), 1) AS pct_account_1,
            count(*) FILTER (WHERE counterparty_account_id IS NOT NULL AND description NOT IN ('Transfer', 'Incoming transfer')) AS bad_counterparties
     FROM transactions;`,
  )
  const [pct, bad] = await output(page).locator('td').allInnerTexts()
  // With 5,000 accounts, account 1 should hold (1/5000)^(1/3), about 5.8%, of the rows.
  expect(Number(pct)).toBeGreaterThan(4)
  expect(Number(pct)).toBeLessThan(8)
  expect(bad).toBe('0')
})

test('reports SQL errors, runs with Ctrl+Enter, and restores the starting query', async ({ page }) => {
  await page.goto(url)

  await editor(page).fill('SELEC 1;')
  await editor(page).press('Control+Enter')
  await expect(playground(page).getByRole('alert')).toContainText('syntax error', STARTUP)

  await playground(page).getByRole('button', { name: 'Restore query' }).click()
  await expect(editor(page)).toHaveValue(/EXPLAIN ANALYZE/)
  await expect(playground(page).getByRole('button', { name: 'Restore query' })).toHaveCount(0)
})
