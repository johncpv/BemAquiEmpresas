import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const PIN_TOKEN = ['e2e', Buffer.from(JSON.stringify({ sub: 'u1', amr: ['pin'] })).toString('base64url'), 'sig'].join('.')

/**
 * PRD-03b CA 1 e 2 contra o build: o /fab é instalável (manifesto "BemAqui Fábrica", standalone, start_url /fab) e o service worker
 * guarda só o shell; depois de usar o /fab, o Cache Storage não tem nenhuma chamada /api/*.
 */
test('PWA do /fab: manifesto instalável e Cache Storage sem /api/*', async ({ page }) => {
  await mockApi(page, {
    'POST /api/v1/auth/refresh': (r: Route) => json(r, { code: 'auth.session_expired' }, 422),
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules() }),
    'GET /api/v1/auth/pin/users': (r: Route) => json(r, { device: { name: 'Tablet Linha 1', theme: 'System' }, users: [{ userId: 'u1', displayName: 'Diego S.' }] }),
    'POST /api/v1/auth/pin': (r: Route) => json(r, { accessToken: PIN_TOKEN, expiresAt: '2030-01-01T00:00:00Z', sessionExpiresAt: '2030-01-01T00:00:00Z' }),
    'GET /api/v1/shopfloor/queue': (r: Route) => json(r, { operator: { employeeId: 'e1', name: 'Diego Santos', firstName: 'Diego', workCenterId: null, workCenterName: null, line: null, today: '2026-10-16' }, current: null, steps: [], serverNow: new Date().toISOString() }),
    'GET /api/v1/shopfloor/device': (r: Route) => json(r, { registered: true, deviceId: 'd1', name: 'Tablet Linha 1', theme: 'System', expiresAt: '2027-04-20T12:00:00Z', hasMyPin: true, canCreatePin: false, pinUnavailableReason: null, canRegister: false, registerUnavailableReason: null }),
  })
  await page.goto('/fab/entrar')

  const href = await page.locator('link[rel="manifest"]').getAttribute('href')
  expect(href).toBeTruthy()
  const manifest = await (await page.request.get(new URL(href!, page.url()).toString())).json()
  expect(manifest).toMatchObject({ name: 'BemAqui Fábrica', display: 'standalone', start_url: '/fab', scope: '/fab' })
  expect((manifest.icons as { sizes: string }[]).map((i) => i.sizes)).toEqual(expect.arrayContaining(['192x192', '512x512']))

  expect(await page.evaluate(async () => (await navigator.serviceWorker.ready).scope)).toMatch(/\/fab$/)
  await page.reload()
  await page.getByRole('button', { name: 'Entendi' }).click()
  await page.getByRole('button', { name: 'Diego S.' }).click()
  const pad = page.getByRole('group', { name: 'Teclado numérico' })
  for (const digit of '483920') await pad.getByRole('button', { name: digit, exact: true }).click()
  await page.getByRole('dialog', { name: 'Olá, Diego S.' }).getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByText('Olá, Diego')).toBeVisible()

  const cached = await page.evaluate(async () => {
    const urls: string[] = []
    for (const name of await caches.keys()) {
      for (const request of await (await caches.open(name)).keys()) urls.push(new URL(request.url).pathname)
    }
    return urls
  })
  expect(cached).toContain('/index.html')
  expect(cached.filter((p) => p.startsWith('/api/') || p.startsWith('/hubs/'))).toEqual([])
})