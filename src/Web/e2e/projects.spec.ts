import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const project = {
  id: 'p1', number: 'PRJ-2026-0001', name: 'Aurora — Elevador', status: 'Planned', partyId: 'c1', partyName: 'Aurora', contractId: 'k1', contractNumber: 'CTR-2026-0001', siteAddressId: null,
  siteAddress: null, promisedDate: '2026-10-01', forecastDate: null, priority: null, managerUserId: 'u1', managerName: 'Ana Souza', createdAt: '2026-09-01T12:00:00Z',
  items: [{ id: 'pi1', sequence: 1, description: 'Elevador', quantity: 1, itemId: null, quoteItemId: 'i1' }], version: 1, fabricatedOn: null, deliveredOn: null, deliveredManually: false,
  hasFabricationMilestone: true, contractStatus: 'Active', isOnboarding: false,
}
const line = (costType: string, budgeted: number, realized: number, committed = 0, includesBackfill = false) => {
  const projected = realized + Math.max(budgeted - realized, committed)
  return { costType, budgeted, realized, committed, projected, deviation: projected - budgeted, deviationPct: budgeted ? ((projected - budgeted) / budgeted) * 100 : null, includesBackfill }
}
const base = (perm: 'owner' | 'engineering') => ({
  'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: perm === 'owner', version: '1', modules: perm === 'owner'
    ? allModules({ projects: { actions: ALL } as never, finance: { actions: ALL } as never })
    : allModules({ projects: { actions: ['view', 'edit'] } as never, finance: { enabled: false, actions: [] } as never }) }),
  'GET /api/v1/projects': (r: Route) => json(r, { items: [{ id: 'p1', number: 'PRJ-2026-0001', name: 'Aurora — Elevador', partyId: 'c1', partyName: 'Aurora', status: 'Planned', promisedDate: '2026-10-01', delayDays: 19, contractValue: 100000, managerUserId: 'u1', managerName: 'Ana Souza', contractId: 'k1' }], page: 1, pageSize: 20, total: 1 }),
  'GET /api/v1/projects/margins': (r: Route) => json(r, [{ id: 'p1', marginPct: 20 }]),
  'GET /api/v1/projects/received': (r: Route) => json(r, [{ id: 'p1', received: 30000, receivedPct: 30 }]),
  'GET /api/v1/projects/p1': (r: Route) => json(r, project),
  'GET /api/v1/projects/p1/costs': (r: Route) => json(r, {
    lines: [line('Material', 30000, 0), line('Labor', 2000, 310.33, 0, true), line('Installation', 10000, 0, 12000)], total: line('Other', 42000, 310.33, 12000), revenue: 100000, budgetedMarginPct: 58, projectedMarginPct: 56,
  }),
  'GET /api/v1/projects/p1/financial': (r: Route) => json(r, { receivables: [], payables: [], received: 30000, toReceive: 70000, paid: 0, toPay: 11000 }),
  'GET /api/v1/projects/p1/timeline': (r: Route) => json(r, [{ at: '2026-09-01T12:00:00Z', kind: 'contract_signed', title: 'Contrato CTR-2026-0001 assinado', who: 'Ana Souza', link: '/app/contratos/k1' }]),
  'GET /api/v1/contracts/k1': (r: Route) => json(r, { id: 'k1', number: 'CTR-2026-0001', quoteId: 'q1', quoteNumber: 'ORC-2026-0009', totalValue: 100000, milestones: [], status: 'Active' }),
  'GET /api/v1/attachments/Project/p1': (r: Route) => json(r, []),
  'GET /api/v1/attachments/Contract/k1': (r: Route) => json(r, []),
  'GET /api/v1/identity/users': (r: Route) => json(r, []),
  'GET /api/v1/quotes/lookups/customers/c1/addresses': (r: Route) => json(r, []),
})

test.describe('Projeto 360 (COM-05)', () => {
  test('lista com atraso e margem; abas pela URL; custos com desvio', async ({ page }, info) => {
    const mobile = info.project.name === 'celular'
    await mockApi(page, base('owner'))
    await page.goto('/app/projetos')
    await expect(page.getByText('19 dias de atraso')).toBeVisible()
    if (!mobile) await expect(page.getByText('20,0%')).toBeVisible()
    await page.goto('/app/projetos/p1?aba=custos')
    await expect(page.locator('main').getByText('Material e horas entram com a produção (Fase 3).', { exact: true })).toHaveCount(0)
    const costs = mobile ? page.getByRole('list', { name: 'Custos por natureza em cartões' }) : page.getByRole('table', { name: 'Custos por natureza' })
    await expect(costs.getByText('Instalação')).toBeVisible()
    // PRD-04 CA 9 e 13: o previsto do instalador está no comprometido e o projetado já mostra o estouro; horas recompostas com a marca.
    await expect(costs.getByText('Calculado na implantação', { exact: true })).toBeVisible()
    if (mobile) {
      const card = costs.getByRole('listitem').filter({ hasText: 'Instalação' })
      await card.getByText('Comprometido e desvio').click()
      await expect(card.getByText(/Comprometido R\$\s12\.000,00/)).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    } else {
      await expect(costs.getByRole('columnheader', { name: /Comprometido/ })).toBeVisible()
      await expect(costs.getByRole('row').filter({ hasText: 'Instalação' }).getByText(/R\$\s12\.000,00/).first()).toBeVisible()
    }
    await page.goto('/app/projetos/p1')
    await expect(page.getByText('Entrega prometida passou há', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Salvar projeto' })).toBeVisible()
  })

  test('engenharia não vê Custos nem Financeiro', async ({ page }) => {
    await mockApi(page, base('engineering'))
    await page.goto('/app/projetos/p1')
    await expect(page.getByRole('tab', { name: 'Linha do tempo' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Custos' })).toHaveCount(0)
    await expect(page.getByRole('tab', { name: 'Financeiro' })).toHaveCount(0)
  })
})
