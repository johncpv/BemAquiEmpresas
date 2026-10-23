import { defineConfig, devices } from '@playwright/test'

/**
 * E2E do front (ADR 0009): desktop e celular a 400px, contra o servidor de desenvolvimento do Vite. O PWA do /fab (PRD-03b) roda
 * contra o build (vite preview), porque o service worker só existe no build.
 */
export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'line' : 'list',
  use: { baseURL: 'http://localhost:5174', trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', testIgnore: /pwa\.spec\.ts/, use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } },
    { name: 'celular', testIgnore: /pwa\.spec\.ts/, use: { ...devices['Desktop Chrome'], viewport: { width: 400, height: 820 }, hasTouch: true, isMobile: true } },
    { name: 'pwa', testMatch: /pwa\.spec\.ts/, use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5175', viewport: { width: 1024, height: 768 }, serviceWorkers: 'allow' } },
  ],
  webServer: [
    { command: 'pnpm exec vite --port 5174 --strictPort', url: 'http://localhost:5174/app', reuseExistingServer: !process.env.CI, timeout: 120_000 },
    { command: 'pnpm exec vite build --outDir dist-e2e --emptyOutDir && pnpm exec vite preview --port 5175 --strictPort --outDir dist-e2e', url: 'http://localhost:5175/fab/entrar', reuseExistingServer: !process.env.CI, timeout: 300_000 },
  ],
})