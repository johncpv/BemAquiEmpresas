import { defineConfig, devices } from '@playwright/test'

/** E2E do front (ADR 0009): desktop e celular a 400px, contra o servidor de desenvolvimento do Vite. */
export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'line' : 'list',
  use: { baseURL: 'http://localhost:5174', trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } },
    { name: 'celular', use: { ...devices['Desktop Chrome'], viewport: { width: 400, height: 820 }, hasTouch: true, isMobile: true } },
  ],
  webServer: { command: 'pnpm exec vite --port 5174 --strictPort', url: 'http://localhost:5174/app', reuseExistingServer: !process.env.CI, timeout: 120_000 },
})
