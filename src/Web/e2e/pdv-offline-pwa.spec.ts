import { expect, test } from '@playwright/test'
import { json, mockApi } from './mock-api'
import { COFFEE, mockPdv, offlineVerifier, OPERATOR, OPERATOR_PIN, openRegister, registerDevice, saleListItem, salePage, scan, storeModules, tapDigits } from './pdv-api'

/**
 * Marco MVP do PDV, fluxo 3 (OFF-02 e OFF-03), contra o build (projeto "pwa"): com rede, o caixa baixa o catálogo com os verificadores sem rede;
 * sem rede, o operador entra pelo PIN conferido no aparelho, abre o caixa e vende; com a rede de volta, a fila sobe sozinha e a venda aparece na
 * retaguarda. Verificação-chave: a venda feita sem rede chega ao servidor (lote assinado do aparelho) e a fila zera.
 */
test('turno sem rede: entra pelo PIN, abre e vende; com a rede de volta, a fila sobe e a venda aparece na retaguarda', async ({ page, context }) => {
  const verifiers = [await offlineVerifier(OPERATOR, OPERATOR_PIN, 'operator', ['cash_out', 'cash_in'])]
  await mockPdv(page, { verifiers })

  // Descida com rede: o aparelho guarda o caixa, o catálogo e os verificadores; o service worker do /pdv/ assume a página.
  await registerDevice(page)
  await expect(page.getByRole('heading', { name: 'Toque no seu nome e digite o seu PIN' })).toBeVisible()
  await page.waitForFunction(() => !!navigator.serviceWorker.controller)

  // Sem rede: a página vem do service worker e a entrada é pelo PIN conferido no aparelho.
  await page.unroute('**/api/v1/**')
  await context.setOffline(true)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Sem conexão: toque no seu nome e digite o seu PIN' })).toBeVisible()
  await page.getByRole('list', { name: 'Quem pode entrar neste caixa' }).getByRole('button', { name: OPERATOR.displayName }).click()
  await tapDigits(page, OPERATOR_PIN)
  await page.getByRole('button', { name: 'Entrar' }).click()

  await openRegister(page, '10000')
  await scan(page, COFFEE.gtin)
  await page.getByRole('button', { name: /Pagar/ }).click()
  await page.getByRole('group', { name: 'Escolha a forma' }).getByRole('button', { name: /Dinheiro/ }).click()
  await page.getByLabel('Valor recebido').fill('20,00')
  await page.getByRole('button', { name: 'Confirmar recebimento' }).click()
  await expect(page.getByRole('heading', { name: 'Venda concluída' })).toBeVisible()
  const status = page.getByRole('status', { name: 'Estado do caixa' })
  // A barra relê a fila a cada 15 s.
  await expect(status).toContainText(/Sem conexão: \d+ vendas? a enviar/, { timeout: 20_000 })

  // Rede de volta: a fila sobe sozinha, na ordem, assinada pelo aparelho, e zera.
  const { uploaded } = await mockPdv(page, { verifiers })
  await context.setOffline(false)
  await expect.poll(() => uploaded.filter((e) => e.kind === 'pos.sale').length, { timeout: 30_000 }).toBeGreaterThan(0)
  await expect(status).not.toContainText(/a enviar/, { timeout: 30_000 })
  expect(uploaded.map((e) => e.seq)).toEqual([...uploaded.map((e) => e.seq)].sort((a, b) => a - b))
  expect(uploaded.some((e) => e.payload.includes('C01-000001'))).toBe(true)

  // Retaguarda: a venda que subiu aparece em Loja → Vendas com o chip de feita sem internet.
  const backoffice = await context.newPage()
  const sale = saleListItem({ id: 'sale-off', number: 'C01-000001', total: 18.99, offline: true })
  await mockApi(backoffice, {
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '1', modules: storeModules() }),
    'GET /api/v1/pos/stores': (r) => json(r, []),
    'GET /api/v1/finance/payment-methods': (r) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    'GET /api/v1/sales/sales': (r) => json(r, salePage([sale])),
  })
  await backoffice.goto('/app/loja/vendas')
  const row = backoffice.getByRole('row').filter({ has: backoffice.getByRole('link', { name: 'C01-000001' }) })
  await expect(row).toBeVisible()
  await expect(row).toContainText('Feita sem internet')
})
