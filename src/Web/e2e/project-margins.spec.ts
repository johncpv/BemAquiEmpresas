import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const OWNER = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const TYPES = ['Material', 'Labor', 'Installation', 'Freight', 'Tax', 'Other'] as const
const line = (costType: string, budgeted: number, realized: number, committed = 0) => {
  const projected = realized + Math.max(budgeted - realized, committed)
  return { costType, budgeted, realized, committed, projected, deviation: projected - budgeted, deviationPct: budgeted ? ((projected - budgeted) / budgeted) * 100 : null, includesBackfill: false }
}
const figures = (revenue: number, lines: ReturnType<typeof line>[], budgetedPct: number, projectedPct: number) => {
  const sum = (k: 'budgeted' | 'realized' | 'committed' | 'projected' | 'deviation') => lines.reduce((s, l) => s + l[k], 0)
  return {
    lines, total: { ...line('Other', sum('budgeted'), sum('realized'), sum('committed')), projected: sum('projected'), deviation: sum('deviation') }, revenue,
    budgetedMargin: revenue - sum('budgeted'), budgetedMarginPct: budgetedPct, realizedMargin: revenue - sum('realized'), realizedMarginPct: null, projectedMargin: revenue - sum('projected'),
    projectedMarginPct: projectedPct, marginDeviationPp: projectedPct - budgetedPct, taxBasis: 'Budgeted', taxGuides: 0,
  }
}
const all = (overrides: Record<string, [number, number, number?]>) => TYPES.map((t) => line(t, ...(overrides[t] ?? [0, 0])))
const p3 = figures(50000, all({ Material: [9000, 6750, 4000], Labor: [3000, 0], Tax: [5000, 0] }), 66, 62.5)
const p1 = figures(100000, all({ Material: [30000, 7249.5], Labor: [5000, 1649.75], Tax: [12000, 0] }), 53, 53)
const header = (id: string, number: string, name: string) => ({ id, number, name, partyId: 'c1', partyName: 'Aurora', status: 'InProgress', managerUserId: 'u1', managerName: 'Ana Souza', contractId: 'k' + id, signedAt: '2026-09-01' })
const ranking = { items: [{ project: header('p3', 'PRJ-2026-0003', 'Compra maior que a baixa'), figures: p3, worstCostType: 'Material' }, { project: header('p1', 'PRJ-2026-0001', 'Material e horas'), figures: p1, worstCostType: null }], page: 1, pageSize: 20, total: 2 }
const portfolio = { projectCount: 2, figures: figures(150000, all({ Material: [39000, 13999.5, 30000], Labor: [8000, 1649.75], Tax: [17000, 0] }), 57.3333, 56.1667), worstCostType: 'Material' }
const entries = (costType: string, column: string, list: { kind: string; document: string; amount: number; link?: string | null; hours?: number | null }[], extra: Record<string, unknown> = {}) => ({
  costType, column, total: list.reduce((s, e) => s + e.amount, 0), entries: list.map((e) => ({ date: '2026-09-02', hours: null, link: null, ...e })), purchasedTotal: null, issuedTotal: null, ...extra,
})
const project = {
  id: 'p3', number: 'PRJ-2026-0003', name: 'Compra maior que a baixa', status: 'InProgress', partyId: 'c1', partyName: 'Aurora', contractId: 'kp3', contractNumber: 'CTR-2026-0003', siteAddressId: null,
  siteAddress: null, promisedDate: null, forecastDate: null, priority: null, managerUserId: 'u1', managerName: 'Ana Souza', createdAt: '2026-09-01T12:00:00Z', items: [], version: 1, fabricatedOn: null,
  deliveredOn: null, deliveredManually: false, hasFabricationMilestone: false, contractStatus: 'Active', isOnboarding: false,
}

const api = (perm: 'owner' | 'engineering' | 'commercial') => ({
  'GET /api/v1/me/permissions': (r: Route) => json(r, {
    permissions: [], isOwner: perm === 'owner', version: '1',
    modules: perm === 'owner' ? allModules({ projects: { actions: OWNER } as never, hr: { actions: ['view', 'view_sensitive'] } as never })
      : perm === 'commercial' ? allModules({ projects: { actions: ['view', 'view_cost'] } as never, hr: { enabled: false, actions: [] } as never, inventory: { enabled: false, actions: [] } as never })
        : allModules({ projects: { actions: ['view', 'edit'] } as never }),
  }),
  'GET /api/v1/projects': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
  'GET /api/v1/projects/margins/ranking': (r: Route) => {
    const sort = new URL(r.request().url()).searchParams.get('sort')
    return json(r, sort === 'number' ? { ...ranking, items: [...ranking.items].reverse() } : ranking)
  },
  'GET /api/v1/projects/margins/portfolio': (r: Route) => json(r, portfolio),
  'GET /api/v1/projects/margins/entries': (r: Route) => json(r, entries('Material', 'Realized', [
    { kind: 'Project', document: 'PRJ-2026-0001 · Material e horas', amount: 7249.5, link: '/app/projetos/p1?aba=custos' },
    { kind: 'Project', document: 'PRJ-2026-0003 · Compra maior que a baixa', amount: 6750, link: '/app/projetos/p3?aba=custos' },
  ])),
  'GET /api/v1/projects/p3': (r: Route) => json(r, project),
  'GET /api/v1/projects/p3/costs': (r: Route) => json(r, { ...p3, nonConformityCount: 1, nonConformityCost: 830, taxGuides: 1500 }),
  'GET /api/v1/projects/p3/costs/Material/entries': (r: Route) => {
    const column = new URL(r.request().url()).searchParams.get('column')
    return json(r, column === 'committed'
      ? entries('Material', 'Committed', [
        { kind: 'PurchasePayable', document: 'CP-0001 · Aço Brasil', amount: 10000, link: '/app/compras/pedidos/po1' },
        { kind: 'StockIssuedOffset', document: 'Baixas líquidas para as OS do projeto', amount: -6000 },
      ], { purchasedTotal: 10000, issuedTotal: 6000 })
      : entries('Material', 'Realized', [
        { kind: 'StockIssue', document: 'Baixa para a OS-2026-0010', amount: 6000, link: perm === 'commercial' ? null : '/app/estoque/movimentos/m1' },
        { kind: 'Payable', document: 'CP-0002 · Concreteira', amount: 750, link: perm === 'commercial' ? null : '/app/financeiro/pagar/cp2' },
      ]))
  },
  'GET /api/v1/projects/p3/costs/Labor/entries': (r: Route) => json(r, entries('Labor', 'Realized', [{ kind: 'Labor', document: 'OS-2026-0010 · Montagem', amount: 1200, hours: 24, link: null }])),
  'GET /api/v1/projects/p3/costs/labor/workers': (r: Route) => json(r, { total: 1200, entries: [{ workerName: 'João Lima', orderNumber: 'OS-2026-0010', workCenterName: 'Montagem', date: '2026-09-02', hours: 24, hourlyCost: 50, amount: 1200, link: null }] }),
  'GET /api/v1/estoque/movimentos/m1': (r: Route) => json(r, {}),
  'GET /api/v1/projects/p3/financial': (r: Route) => json(r, { receivables: [], payables: [], received: 0, toReceive: 0, paid: 0, toPay: 0 }),
  'GET /api/v1/projects/p3/timeline': (r: Route) => json(r, []),
  'GET /api/v1/contracts/kp3': (r: Route) => json(r, { id: 'kp3', number: 'CTR-2026-0003', quoteId: 'q3', quoteNumber: 'ORC-2026-0003', totalValue: 50000, milestones: [], status: 'Active' }),
  'GET /api/v1/attachments/Project/p3': (r: Route) => json(r, []),
  'GET /api/v1/attachments/Contract/kp3': (r: Route) => json(r, []),
})

test.describe('Orçado × realizado (DSH-02)', () => {
  test('relatório ranqueado, ordenação, drill-down até o documento e 400 px', async ({ page }, info) => {
    const mobile = info.project.name === 'celular'
    await mockApi(page, api('owner'))
    await page.goto('/app/projetos/margens')
    await expect(page.getByRole('heading', { level: 1, name: 'Quanto cada projeto está rendendo de verdade.' })).toBeVisible()
    const consolidated = page.getByRole('region', { name: 'Consolidado' })
    await expect(consolidated.getByText('Margem orçada → projetada')).toBeVisible()
    await expect(consolidated.getByText('57,3% → 56,2%')).toBeVisible()
    await expect(page.getByText('A natureza que mais desviou foi Material', { exact: false })).toBeVisible()
    await expect(page.getByText('Margem 3,5 pp abaixo')).toBeVisible()

    // Pior desvio primeiro; ordenar pelo número inverte a lista.
    const table = page.getByRole('table', { name: 'Projetos do pior para o melhor desvio' })
    await expect(table.getByRole('row').nth(1)).toContainText('PRJ-2026-0003')
    await table.getByRole('button', { name: /Projeto/ }).click()
    await expect(page).toHaveURL(/sort=number/)
    await expect(table.getByRole('row').nth(1)).toContainText('PRJ-2026-0001')

    // Drill-down do consolidado: total igual ao da célula e link para a aba Custos do projeto.
    const nature = mobile ? page.getByRole('list', { name: 'Orçado × realizado por natureza em cartões' }) : page.getByRole('table', { name: 'Orçado × realizado por natureza' })
    await nature.getByRole('button', { name: 'Realizado de Material: ver origem' }).click()
    const panel = page.getByRole('dialog', { name: /Realizado de Material/ })
    await expect(panel.getByText(/R\$\s13\.999,50/).last()).toBeVisible()
    if (mobile) {
      const box = await panel.boundingBox()
      expect(box?.width).toBeGreaterThanOrEqual(399)
    }
    await panel.getByRole('link', { name: 'PRJ-2026-0003 · Compra maior que a baixa' }).click()
    await expect(page).toHaveURL(/\/app\/projetos\/p3\?aba=custos/)

    // Aba Custos: comprometido de material por saldo, notas da guia e da NC, e o documento de origem.
    const costs = mobile ? page.getByRole('list', { name: 'Custos por natureza em cartões' }) : page.getByRole('table', { name: 'Custos por natureza' })
    await expect(page.getByText('Guias de imposto são da empresa: entram no DRE, não no projeto.')).toBeVisible()
    await expect(page.getByText(/Não conformidades: 1, custo estimado R\$\s830,00; o retrabalho já está nas horas e no material\./)).toBeVisible()
    await expect(costs.getByText('Imposto orçado').first()).toBeVisible()
    if (mobile) await costs.getByRole('listitem').filter({ hasText: 'Material' }).getByText('Comprometido e desvio').click()
    await costs.getByRole('button', { name: 'Comprometido de Material: ver origem' }).click()
    const committed = page.getByRole('dialog', { name: /Comprometido de Material/ })
    await expect(committed.getByRole('note')).toContainText(/Títulos de compra R\$\s10\.000,00 − baixas R\$\s6\.000,00 = R\$\s4\.000,00/)
    await committed.getByRole('button', { name: 'Fechar' }).click()
    await costs.getByRole('button', { name: 'Realizado de Material: ver origem' }).click()
    await expect(page.getByRole('dialog').getByRole('link', { name: 'Baixa para a OS-2026-0010' })).toHaveAttribute('href', '/app/estoque/movimentos/m1')
    await page.getByRole('dialog').getByRole('button', { name: 'Fechar' }).click()
    await costs.getByRole('button', { name: 'Realizado de Mão de obra: ver origem' }).click()
    await expect(page.getByRole('dialog').getByText('João Lima', { exact: false })).toBeVisible()
    if (mobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  })

  test('sem hr.view_sensitive as horas ficam por centro, com Dados protegidos, e o documento sem link', async ({ page }, info) => {
    const mobile = info.project.name === 'celular'
    let askedWorkers = false
    await mockApi(page, { ...api('commercial'), 'GET /api/v1/projects/p3/costs/labor/workers': (r: Route) => { askedWorkers = true; return json(r, {}, 403) } })
    await page.goto('/app/projetos/p3?aba=custos')
    const costs = mobile ? page.getByRole('list', { name: 'Custos por natureza em cartões' }) : page.getByRole('table', { name: 'Custos por natureza' })
    await costs.getByRole('button', { name: 'Realizado de Mão de obra: ver origem' }).click()
    const panel = page.getByRole('dialog', { name: /Realizado de Mão de obra/ })
    await expect(panel.getByText('OS-2026-0010 · Montagem')).toBeVisible()
    await expect(panel.getByText('Dados protegidos')).toBeVisible()
    await expect(panel.getByText('João Lima')).toHaveCount(0)
    expect(askedWorkers).toBe(false)
    await panel.getByRole('button', { name: 'Fechar' }).click()
    await costs.getByRole('button', { name: 'Realizado de Material: ver origem' }).click()
    await expect(page.getByRole('dialog').getByText('Baixa para a OS-2026-0010')).toBeVisible()
    await expect(page.getByRole('dialog').getByRole('link', { name: 'Baixa para a OS-2026-0010' })).toHaveCount(0)
  })

  test('Engenharia não vê o item no menu e a tela nega o acesso', async ({ page }) => {
    await mockApi(page, api('engineering'))
    await page.goto('/app/projetos')
    await expect(page.getByRole('heading', { level: 1, name: 'Comercial: Projetos' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Orçado × realizado' })).toHaveCount(0)
    await page.goto('/app/projetos/margens')
    await expect(page.getByRole('heading', { level: 1, name: 'Sem acesso a esta tela' })).toBeVisible()
  })
})
