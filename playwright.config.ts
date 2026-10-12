import { defineConfig, devices } from '@playwright/test'

// Tests run against the production build (`npm run test:e2e` builds first),
// served by `vitepress preview`, so they check what actually ships.
// Not 4173 (the `docs:preview` default), so a preview you have open is never reused.
const port = 4319

export default defineConfig({
  testDir: 'tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
    // `vitepress preview` brotli-compresses each response on the fly, which takes
    // seconds for the Mermaid chunk. GitHub Pages doesn't, so ask for gzip.
    extraHTTPHeaders: { 'Accept-Encoding': 'gzip' },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `npx vitepress preview docs --port ${port}`,
    url: `http://localhost:${port}`,
    // Always start fresh: a server started before the last build serves stale assets.
    reuseExistingServer: false,
  },
})
