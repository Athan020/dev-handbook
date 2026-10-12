import { expect, test, type Page } from '@playwright/test'

// The live example on the module-format guide page.
const url = '/guide/module-format'
const modelAnswer = 'An average hides the slowest requests'

const block = (page: Page) => page.locator('.stop-and-think').first()
const answerBox = (page: Page) => block(page).getByLabel('Your answer, before you look')
const revealButton = (page: Page) => block(page).getByRole('button', { name: 'Compare with the answer' })

test('model answer stays hidden until the reader commits to an answer', async ({ page }) => {
  await page.goto(url)
  await expect(block(page).getByText(modelAnswer)).toBeHidden()
  await expect(revealButton(page)).toBeDisabled()

  await answerBox(page).fill('   ')
  await expect(revealButton(page), 'whitespace is not an answer').toBeDisabled()

  await answerBox(page).fill('The average hides slow outliers.')
  await revealButton(page).click()

  await expect(block(page).getByText(modelAnswer)).toBeVisible()
  await expect(block(page).getByText('The average hides slow outliers.')).toBeVisible()
  await expect(block(page).locator('.revealed'), 'focus moves to the revealed answer').toBeFocused()
})

test('the answer and revealed state survive a reload, and "Try again" resets them', async ({ page }) => {
  await page.goto(url)
  await answerBox(page).fill('Percentiles, not averages.')
  await revealButton(page).click()
  await expect(block(page).getByText(modelAnswer)).toBeVisible()

  await page.reload()
  await expect(block(page).getByText(modelAnswer)).toBeVisible()
  await expect(block(page).getByText('Percentiles, not averages.')).toBeVisible()

  await block(page).getByRole('button', { name: 'Try again' }).click()
  await expect(block(page).getByText(modelAnswer)).toBeHidden()
  await expect(answerBox(page)).toHaveValue('')
  await page.reload()
  await expect(block(page).getByText(modelAnswer)).toBeHidden()
})

test('the reader can skip without answering', async ({ page }) => {
  await page.goto(url)
  await block(page).getByRole('button', { name: 'Skip and show the answer' }).click()
  await expect(block(page).getByText(modelAnswer)).toBeVisible()
  await expect(block(page).getByText('Your answer', { exact: true }), 'no empty "your answer" panel').toHaveCount(0)
})

test('works when localStorage is unavailable', async ({ page }) => {
  const errors: string[] = []
  page.on('pageerror', (e) => errors.push(e.message))
  // Block storage for our keys only. VitePress's own inline theme script also reads
  // localStorage, unguarded, and would throw first; that's outside our control.
  await page.addInitScript(() => {
    for (const method of ['getItem', 'setItem'] as const) {
      const original = Storage.prototype[method]
      Storage.prototype[method] = function (this: Storage, key: string, ...rest: string[]) {
        if (key.startsWith('handbook:')) throw new DOMException('Storage blocked', 'SecurityError')
        return (original as (...a: string[]) => unknown).call(this, key, ...rest)
      } as never
    }
  })
  await page.goto(url)
  await answerBox(page).fill('Still works.')
  await revealButton(page).click()
  await expect(block(page).getByText(modelAnswer)).toBeVisible()
  expect(errors).toEqual([])
})
