import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const funnel = {
  from: '2026-07-23', to: '2026-10-21', authorId: null,
  stages: [
    { status: 'Draft', situation: 'rascunho', count: 2, amount: 300 },
    { status: 'Sent', situation: 'enviado', count: 2, amount: 1400 },
    { status: 'Approved', situation: 'aprovado', count: 2, amount: 4000 },
    { status: 'Rejected', situation: 'perdido', count: 3, amount: 1500 },
    { status: 'Expired', situation: 'expirado', count: 1, amount: 1000 },
  ],
  conversion: { byCount: 33.3, byAmount: 61.5, decided: 6 },
  lossReasons: [{ reason: 'Price', count: 2, amount: 1200, pct: 66.7 }, { reason: 'Other', count: 1, amount: 300, pct: 33.3 }],
  otherLosses: [{ id: 'q9', number: 'ORC-2026-0009', revision: 0, detail: 'Cliente mudou o projeto', totalPrice: 300 }],
  expiring: [{ id: 'q1', number: 'ORC-2026-0001', revision: 0, partyId: 'p1', partyName: 'Condomínio Aurora', totalPrice: 1000, validUntil: '2026-10-24', daysLeft: 3 }],
  authors: [{ id: 'u1', name: 'Ana' }],
}

test.describe('Funil comercial (COM-08)', () => {
  test('mostra situações, conversão, motivos e vencendo; o clique abre a lista filtrada', async ({ page }) => {
    let listUrl = ''
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ quotes: { actions: ALL } as never }) }),
      'GET /api/v1/quotes/funnel': (r: Route) => json(r, funnel),
      'GET /api/v1/quotes': (r: Route) => { listUrl = r.request().url(); return json(r, { items: [], page: 1, pageSize: 20, total: 0 }) },
    })
    await page.goto('/app/orcamentos/funil')
    const main = page.getByRole('main')
    const conversion = main.getByRole('region', { name: 'Taxa de conversão' })
    await expect(conversion.getByText('33,3%')).toBeVisible()
    await expect(conversion.getByText('61,5%')).toBeVisible()
    await expect(main.getByRole('cell', { name: 'Preço', exact: true })).toBeVisible()
    await expect(main.getByText('ORC-2026-0001')).toBeVisible()
    await expect(main.getByRole('link', { name: 'Prorrogar validade' })).toHaveAttribute('href', /acao=prorrogar/)
    await main.getByRole('link', { name: /Aprovado/ }).click()
    await expect(page).toHaveURL(/situacao=aprovado/)
    await expect(page.getByText('Filtrado pelo funil', { exact: false })).toBeVisible()
    await expect.poll(() => listUrl).toContain('situation=aprovado')
    expect(listUrl).toContain('from=2026-07-23')
  })

  test('vazio oferece os últimos 12 meses e cabe em 400 px', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ quotes: { actions: ALL } as never }) }),
      'GET /api/v1/quotes/funnel': (r: Route) => json(r, { ...funnel, stages: funnel.stages.map((s) => ({ ...s, count: 0, amount: 0 })), lossReasons: [], otherLosses: [], expiring: [], conversion: { byCount: null, byAmount: null, decided: 0 } }),
    })
    await page.goto('/app/orcamentos/funil')
    await expect(page.getByText('Sem orçamentos no período.')).toBeVisible()
    await page.getByRole('button', { name: 'Ver últimos 12 meses' }).click()
    await expect(page).toHaveURL(/de=2025-10-21/)
    const width = await page.evaluate(() => document.documentElement.scrollWidth)
    expect(width).toBeLessThanOrEqual(400)
  })
})
