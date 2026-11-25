import { expect, test } from '@playwright/test'
import { mockPdv, registerDevice, waitForController } from './pdv-api'

/**
 * OFF-01 CA 1 e 2 contra o build (projeto "pwa", vite preview): o /pdv/ tem manifesto e service worker próprios, de escopo /pdv/; aberto uma vez
 * com rede, abre sem rede mostrando o caixa, a loja e a faixa "Sem conexão".
 */

test('PWA do /pdv: manifesto e service worker próprios; abre sem rede depois da primeira abertura', async ({ page, context }) => {
  await mockPdv(page)

  await page.goto('/pdv/')
  await expect(page.getByRole('heading', { name: 'Este aparelho ainda não é um caixa.' })).toBeVisible()

  const href = await page.locator('link[rel="manifest"]').getAttribute('href')
  expect(href).toBe('/pdv/manifest.webmanifest')
  const manifest = await (await page.request.get(new URL(href!, page.url()).toString())).json()
  expect(manifest).toMatchObject({ name: 'BemAqui Caixa', display: 'standalone', start_url: '/pdv/', scope: '/pdv/' })
  expect(await page.evaluate(async () => (await navigator.serviceWorker.ready).scope)).toMatch(/\/pdv\/$/)
  await waitForController(page)

  // Aparelho registrado: a chave no banco dele, o aperto de mão e a descida do catálogo com rede.
  await registerDevice(page)
  await expect(page.getByRole('heading', { name: 'Caixa 01 · Loja Centro' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Diego S.' })).toBeVisible()

  // Sem rede: a página vem do service worker e o caixa e a loja vêm do aparelho.
  await page.unroute('**/api/v1/**')
  await context.setOffline(true)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Caixa 01 · Loja Centro' })).toBeVisible()
  await expect(page.getByRole('status', { name: 'Estado do caixa' })).toContainText('Sem conexão')
  await expect(page.getByRole('heading', { name: 'Sem conexão: toque no seu nome e digite o seu PIN' })).toBeVisible()

  // O service worker do /fab não foi registrado por aqui; o do caixa é o único desta página.
  const scopes = await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).map((r) => new URL(r.scope).pathname))
  expect(scopes).toEqual(['/pdv/'])
  await context.setOffline(false)
})
