import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

/**
 * OFF-01 CA 1 e 2 contra o build (projeto "pwa", vite preview): o /pdv/ tem manifesto e service worker próprios, de escopo /pdv/; aberto uma vez
 * com rede, abre sem rede mostrando o caixa, a loja e a faixa "Sem conexão".
 */
const HANDSHAKE = {
  deviceId: 'd1', storeId: 's1', storeCode: 'CENTRO', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Touch', salePrefix: 'C01-', serverTime: new Date().toISOString(),
}

test('PWA do /pdv: manifesto e service worker próprios; abre sem rede depois da primeira abertura', async ({ page, context }) => {
  await mockApi(page, {
    'POST /api/v1/auth/refresh': (r: Route) => json(r, { code: 'auth.session_expired' }, 422),
    'POST /api/v1/pos/sync/handshake': (r: Route) => json(r, HANDSHAKE),
    'GET /api/v1/pos/sync/operators': (r: Route) => json(r, { operators: [{ userId: 'u1', displayName: 'Diego S.' }] }),
  })

  await page.goto('/pdv/')
  await expect(page.getByRole('heading', { name: 'Este aparelho ainda não é um caixa.' })).toBeVisible()

  const href = await page.locator('link[rel="manifest"]').getAttribute('href')
  expect(href).toBe('/pdv/manifest.webmanifest')
  const manifest = await (await page.request.get(new URL(href!, page.url()).toString())).json()
  expect(manifest).toMatchObject({ name: 'BemAqui Caixa', display: 'standalone', start_url: '/pdv/', scope: '/pdv/' })
  expect(await page.evaluate(async () => (await navigator.serviceWorker.ready).scope)).toMatch(/\/pdv\/$/)
  await page.waitForFunction(() => !!navigator.serviceWorker.controller)

  // Aparelho registrado: a chave do aparelho no banco dele (como o registro do PDV-01 deixa) e o aperto de mão com rede.
  await page.evaluate(async () => {
    const keys = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign', 'verify'])
    await new Promise<void>((resolve, reject) => {
      const open = indexedDB.open('bemaqui-pdv', 1)
      open.onupgradeneeded = () => open.result.createObjectStore('device')
      open.onerror = () => reject(open.error)
      open.onsuccess = () => {
        const tx = open.result.transaction('device', 'readwrite')
        tx.objectStore('device').put(keys, 'key')
        tx.oncomplete = () => { open.result.close(); resolve() }
        tx.onerror = () => reject(tx.error)
      }
    })
  })
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Caixa 01 · Loja Centro' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Diego S.' })).toBeVisible()

  // Sem rede: a página vem do service worker e o caixa e a loja vêm do aparelho.
  await page.unroute('**/api/v1/**')
  await context.setOffline(true)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Caixa 01 · Loja Centro' })).toBeVisible()
  await expect(page.getByRole('status', { name: 'Estado do caixa' })).toContainText('Sem conexão')
  await expect(page.getByRole('heading', { name: 'Sem conexão.' })).toBeVisible()

  // O service worker do /fab não foi registrado por aqui; o do caixa é o único desta página.
  const scopes = await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).map((r) => new URL(r.scope).pathname))
  expect(scopes).toEqual(['/pdv/'])
  await context.setOffline(false)
})
