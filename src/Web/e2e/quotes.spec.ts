import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const withCost = allModules().map((m) => (m.code === 'quotes' ? { ...m, actions: [...m.actions, 'view_cost'] } : m))
const perms = (modules: unknown) => (r: Route) => json(r, { permissions: [], isOwner: false, version: '9', modules })

function quotesApi() {
  const state = { version: 1, cost: 0 }
  const quote = () => ({
    id: 'q1', number: 'ORC-2026-0001', revision: 0, status: 'Draft', partyId: 'c1', partyName: 'Aurora Condomínio', contactId: null, siteAddressId: null, deliveryDays: null, notes: null,
    pricingMode: 'Markup', items: [{ id: 'i1', sequence: 1, kind: 'Equipment', itemId: null, itemName: null, description: 'Elevador 6 paradas', quantity: 1, unitPrice: state.cost * 1.4, priceOverridden: false, total: state.cost * 1.4 }],
    totalPrice: state.cost * 1.4, sentAt: null, createdAt: '2026-10-20T10:00:00Z', version: state.version,
  })
  const costs = () => ({ quoteId: 'q1', pricingMode: 'Markup', defaultPct: 40, totalCost: state.cost, totalPrice: state.cost * 1.4, marginPct: state.cost ? 28.5714 : null,
    items: [{ itemId: 'i1', unitCost: state.cost, pct: null, effectivePct: 40, suggestedPrice: state.cost * 1.4, lines: [] }] })
  const posted: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/quotes': (r) => json(r, { items: [{ id: 'q1', number: 'ORC-2026-0001', revision: 0, status: 'Draft', partyId: 'c1', partyName: 'Aurora Condomínio', mainItem: 'Elevador 6 paradas', totalPrice: 1400, sentAt: null, createdAt: '2026-10-20T10:00:00Z' }], page: 1, pageSize: 20, total: 1 }),
    'GET /api/v1/quotes/costs': (r) => json(r, [{ id: 'q1', totalCost: 1000, marginPct: 28.5714 }]),
    'GET /api/v1/quotes/q1': (r) => json(r, quote()),
    'GET /api/v1/quotes/q1/costs': (r) => json(r, costs()),
    'PUT /api/v1/quotes/q1/items/i1/costs': (r) => { posted.push(r.request().postDataJSON()); state.cost = 1000; state.version++; return json(r, costs()) },
    'GET /api/v1/quotes/lookups/positions': (r) => json(r, [{ id: 'p1', name: 'Montador', defaultWorkCenterId: null, hourlyCost: 50 }]),
    'GET /api/v1/quotes/lookups/work-centers': (r) => json(r, []),
    'GET /api/v1/quotes/lookups/customers/*/contacts': (r) => json(r, []),
    'GET /api/v1/quotes/lookups/customers/*/addresses': (r) => json(r, []),
  }
  return { api, posted }
}

test.describe('Orçamentos (COM-01a)', () => {
  test('composição pelo teclado com subtotal e preço sugerido ao vivo (CA 8, 12 e 15)', async ({ page }, info) => {
    test.skip(info.project.name === 'celular')
    const { api, posted } = quotesApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': perms(withCost) })
    await page.goto('/app/orcamentos/q1?aba=itens')
    await page.getByRole('button', { name: 'Composição de custo' }).click()
    const labor = page.getByRole('region', { name: 'Mão de obra' })
    await labor.getByRole('button', { name: /Adicionar linha de mão de obra/ }).click()
    await labor.getByRole('combobox', { name: 'Cargo da linha 1' }).selectOption('p1')
    await labor.getByRole('textbox', { name: 'Horas da linha 1' }).fill('8')
    await labor.getByRole('textbox', { name: 'Horas da linha 1' }).blur()
    await expect(labor.getByText(/Subtotal\s*R\$\s400,00/)).toBeVisible()
    const freight = page.getByRole('region', { name: 'Frete' })
    await freight.getByRole('button', { name: /Adicionar linha de frete/ }).click()
    await freight.getByRole('textbox', { name: 'Valor da linha 1' }).fill('600')
    await freight.getByRole('textbox', { name: 'Valor da linha 1' }).blur()
    await expect(page.getByText(/Preço sugerido:\s*R\$\s1\.400,00/)).toBeVisible()
    await expect(page.getByText('Markup 40,00% · Margem 28,57%', { exact: true })).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await page.getByRole('button', { name: 'Salvar composição' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Composição salva' }).first()).toBeVisible()
    expect(posted[0]).toMatchObject({ lines: [{ costType: 'Labor', positionId: 'p1', quantity: 8, unitCost: null }, { costType: 'Freight', quantity: 1, unitCost: 600 }] })
  })

  test('sem ver custo, a lista e o orçamento não mostram custo nem margem (CA 2)', async ({ page }) => {
    const { api } = quotesApi()
    await mockApi(page, api)
    await page.goto('/app/orcamentos')
    await expect(page.getByRole('link', { name: 'ORC-2026-0001' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Margem' })).toHaveCount(0)
    await page.goto('/app/orcamentos/q1?aba=itens')
    await expect(page.getByRole('button', { name: 'Composição de custo' })).toHaveCount(0)
    await expect(page.getByText('Custo total')).toHaveCount(0)
  })
})
