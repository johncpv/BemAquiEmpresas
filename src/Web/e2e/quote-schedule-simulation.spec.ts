import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const perms = (modules: unknown) => (r: Route) => json(r, { permissions: [], isOwner: false, version: '9', modules })
const withCost = allModules().map((m) => (m.code === 'quotes' ? { ...m, actions: [...m.actions, 'view_cost'] } : m))

const quote = (over: Record<string, unknown> = {}) => ({
  id: 'q1', number: 'ORC-2026-0009', revision: 0, status: 'Draft', partyId: 'c1', partyName: 'Aurora Condomínio', contactId: null, siteAddressId: null, deliveryDays: 5, notes: null,
  pricingMode: 'Markup', items: [{ id: 'i1', sequence: 1, kind: 'Equipment', itemId: null, itemName: null, description: 'Elevador 6 paradas', quantity: 1, unitPrice: 1400, priceOverridden: false, total: 1400 }],
  totalPrice: 1400, sentAt: null, createdAt: '2026-10-20T10:00:00Z', validUntil: '2026-11-15', lostReason: null, lostDetail: null, isCurrent: true, parentQuoteId: null, copiedFromNumber: null,
  version: 1, ...over,
})

const atEnd = {
  simulated: true, queueSize: 3, queuePosition: 4, atEnd: true, today: '2026-10-30', fabricationEnd: '2026-11-05', deliveryDate: '2026-11-12', deliveryDays: 13, noForecastReason: null,
  includesInstallation: true, promisedDays: 5, promisedDate: '2026-11-04', daysBeyondPromise: 8, newlyLateCount: 0, newlyLateProjects: [],
}
const atFirst = {
  ...atEnd, queuePosition: 1, atEnd: false, fabricationEnd: '2026-11-02', deliveryDate: '2026-11-09', deliveryDays: 10, daysBeyondPromise: 5, newlyLateCount: 2,
  newlyLateProjects: [{ projectId: 'p1', number: 'PRJ-2026-0001', name: 'Torre A' }, { projectId: 'p2', number: 'PRJ-2026-0002', name: 'Torre B' }],
}
const noHours = { ...atEnd, simulated: false, queueSize: 0, queuePosition: 0, fabricationEnd: null, deliveryDate: null, deliveryDays: null, promisedDate: null, daysBeyondPromise: null }

function simulationApi(current = quote(), respond: (body: { queuePosition: number | null }) => { status?: number; body: unknown } = (b) => ({ body: b.queuePosition === 1 ? atFirst : atEnd })) {
  const simulated: unknown[] = []
  const saved: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/quotes/q1': (r) => json(r, current),
    'GET /api/v1/quotes/q1/costs': (r) => json(r, { quoteId: 'q1', pricingMode: 'Markup', defaultPct: 40, totalCost: 1000, totalPrice: 1400, marginPct: 28.57, items: [], staleLaborCosts: false }),
    'GET /api/v1/quotes/lookups/customers/*/contacts': (r) => json(r, []),
    'GET /api/v1/quotes/lookups/customers/*/addresses': (r) => json(r, []),
    'GET /api/v1/quotes/*/conditions': (r) => json(r, { milestones: [], percentSum: 0, warning: null, validUntil: '2026-11-15', defaultPaymentTermId: null, version: 1 }),
    'POST /api/v1/quotes/q1/schedule-simulation': (r) => {
      const body = r.request().postDataJSON() as { queuePosition: number | null }
      simulated.push(body)
      const { status = 200, body: answer } = respond(body)
      return json(r, answer, status)
    },
    'PUT /api/v1/quotes/q1': (r) => { const body = r.request().postDataJSON() as Record<string, unknown>; saved.push(body); return json(r, { ...current, deliveryDays: body.deliveryDays, version: 2 }) },
  }
  return { api, simulated, saved }
}

test.describe('Simulação de prazo do orçamento (PLN-05)', () => {
  test('simula no fim e na posição 1, usa o prazo e só grava ao salvar (CA 3, 4, 6 e 8)', async ({ page }, info) => {
    const { api, simulated, saved } = simulationApi()
    await mockApi(page, api)
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Simular prazo', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Simular prazo' })
    await expect(panel.getByText('Se aprovado hoje, a fabricação termina em 05/11/2026 e a entrega fica em 12/11/2026, com o prazo de instalação.')).toBeVisible()
    await expect(panel.getByText('Prazo prometido no orçamento: 5 dias → 04/11/2026')).toBeVisible()
    await expect(panel.getByText('8 dias além do prazo')).toBeVisible()
    await expect(panel.getByText('Não considera o prazo de compra do material.')).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Ver capacidade de produzir' })).toBeVisible()
    await expect(panel.getByText(/Projetos que passam a atrasar/)).toHaveCount(0)
    expect(simulated[0]).toEqual({ queuePosition: null })

    await panel.getByRole('radio', { name: 'Na posição' }).check()
    await panel.getByRole('textbox', { name: 'Posição na fila' }).fill('1')
    await panel.getByRole('button', { name: 'Simular de novo' }).click()
    await expect(panel.getByText('Projetos que passam a atrasar: 2 (PRJ-2026-0001 Torre A, PRJ-2026-0002 Torre B)')).toBeVisible()
    await expect(panel.getByText('Na posição 1 de 4')).toBeVisible()
    expect(simulated[1]).toEqual({ queuePosition: 1 })

    const use = panel.getByRole('button', { name: 'Usar este prazo' })
    if (info.project.name === 'celular') {
      const box = await panel.boundingBox()
      expect(box?.width).toBeGreaterThanOrEqual(395)
      expect((await use.boundingBox())?.height).toBeGreaterThanOrEqual(44)
    } else {
      const results = await new AxeBuilder({ page }).include('[role="dialog"]').analyze()
      expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    }

    await use.click()
    await expect(panel).toHaveCount(0)
    const field = page.getByRole('textbox', { name: 'Prazo de entrega prometido (dias)' })
    await expect(field).toHaveValue('10')
    await expect(page.getByText('Preenchido pela simulação de prazo. Salve o orçamento para gravar.')).toBeVisible()
    expect(saved).toHaveLength(0)
    await page.getByRole('button', { name: 'Salvar orçamento' }).click()
    await expect.poll(() => saved.length).toBe(1)
    expect(saved[0]).toMatchObject({ deliveryDays: 10, version: 1 })
  })

  test('sem horas por centro mostra o que fazer; com custo leva à composição (CA 7)', async ({ page }) => {
    const { api } = simulationApi(quote(), () => ({ body: noHours }))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': perms(withCost) })
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Simular prazo', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Simular prazo' })
    await expect(panel.getByText('Este orçamento não tem horas por centro de trabalho. Informe as horas na composição de custo para simular.')).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Usar este prazo' })).toHaveCount(0)
    await panel.getByRole('button', { name: 'Abrir a composição de custo' }).click()
    await expect(page).toHaveURL(/aba=itens/)
  })

  test('enviado cria nova revisão já com o prazo no campo (CA 6)', async ({ page }) => {
    const sent = quote({ status: 'Sent', sentAt: '2026-10-21T10:00:00Z' })
    const { api } = simulationApi(sent)
    let revised = 0
    await mockApi(page, {
      ...api,
      'POST /api/v1/quotes/q1/revisions': (r) => { revised++; return json(r, { ...quote({ id: 'q2', revision: 1, parentQuoteId: 'q1' }) }, 201) },
      'GET /api/v1/quotes/q2': (r) => json(r, quote({ id: 'q2', revision: 1, parentQuoteId: 'q1' })),
      'GET /api/v1/quotes/q2/costs': (r) => json(r, { quoteId: 'q2', pricingMode: 'Markup', defaultPct: 40, totalCost: 0, totalPrice: 0, marginPct: null, items: [], staleLaborCosts: false }),
    })
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Simular prazo', exact: true }).click()
    await page.getByRole('dialog', { name: 'Simular prazo' }).getByRole('button', { name: 'Criar nova revisão com este prazo' }).click()
    await expect(page).toHaveURL(/\/app\/orcamentos\/q2/)
    expect(revised).toBe(1)
    await expect(page.getByRole('textbox', { name: 'Prazo de entrega prometido (dias)' })).toHaveValue('13')
  })

  test('limite de simulações e erro com Tentar de novo (CA 10)', async ({ page }) => {
    let calls = 0
    const { api } = simulationApi(quote(), () => {
      calls++
      return calls === 1 ? { status: 429, body: { status: 429, title: 'Aguarde alguns segundos para simular de novo.', code: 'rate_limited' } }
        : calls === 2 ? { status: 500, body: { status: 500, title: 'Erro' } } : { body: atEnd }
    })
    await mockApi(page, api)
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Simular prazo', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Simular prazo' })
    await expect(panel.getByText('Aguarde alguns segundos para simular de novo.')).toBeVisible()
    await panel.getByRole('button', { name: 'Fechar' }).click()
    await page.getByRole('button', { name: 'Simular prazo', exact: true }).click()
    await expect(panel.getByText('Não foi possível simular o prazo.')).toBeVisible()
    await panel.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(panel.getByText(/Se aprovado hoje/)).toBeVisible()
  })

  test('sem o módulo de Produção liberado, o botão não aparece (CA 11)', async ({ page }) => {
    const { api } = simulationApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': perms(allModules({ production: { released: false } })) })
    await page.goto('/app/orcamentos/q1')
    await expect(page.getByRole('button', { name: 'Salvar orçamento' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Simular prazo', exact: true })).toHaveCount(0)
  })
})
