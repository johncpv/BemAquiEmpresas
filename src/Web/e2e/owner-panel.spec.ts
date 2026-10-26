import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const iso = (offset: number) => new Date(Date.now() + offset * 86_400_000).toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
const at = new Date().toISOString()
const OWNER = ['balances', 'revenue', 'overdue', 'margin', 'active-projects', 'production', 'backlog', 'deliveries']
const FINANCE = ['balances', 'revenue', 'overdue', 'active-projects', 'backlog', 'deliveries']
const ACTIONS = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const m3 = iso(0).slice(0, 7)
const monthOf = (offset: number) => { const d = new Date(`${m3}-15T12:00:00Z`); d.setUTCMonth(d.getUTCMonth() + offset); return d.toISOString().slice(0, 7) }
const MONTHS = [monthOf(-2), monthOf(-1), m3]

const overview = (cards: string[]) => ({
  greeting: 'Bom dia', firstName: 'Ana', today: iso(0), summary: null, cards, generatedAt: at,
  attention: cards.includes('deliveries') ? [{ code: 'project_late', tone: 'crit', title: 'Ed. Monte Castelo vai atrasar 7 dias', detail: 'Previsão 10/11, prometido ao cliente 03/11.', url: '/app/projetos/p2?aba=cronograma' }] : [],
})

const CARDS: Record<string, unknown> = {
  balances: { date: iso(0), consolidated: 214_380, accounts: [{ id: 'b1', name: 'Inter', balance: 214_380 }], openingMissing: false, updatedAt: at },
  revenue: { year: 2026, month: 10, available: true, monthAmount: 312_600, yearAmount: 2_900_000, monthTarget: null, yearTarget: null, monthPct: null, yearPct: null, provisional: false, workingDaysLeft: 5, canEditTargets: false, updatedAt: at },
  overdue: { today: iso(0), total: 0, count: 0, customers: 0, maxDaysLate: 0, updatedAt: at, largest: null, buckets: [], topCustomers: [] },
  margin: {
    year: 2026, activeCount: 4, activeRevenue: 1_200_000, projectedMarginPct: 19.4, budgetedMarginPct: 23, deviationPp: -3.6, finishedCount: 2, finishedRealizedMarginPct: 21.25,
    finishedBudgetedMarginPct: 22, finishedDeviationPp: -0.75, updatedAt: at,
    worst: [{ projectId: 'p2', number: 'PRJ-2026-0002', name: 'Ed. Monte Castelo', partyName: 'Monte Castelo', budgetedMarginPct: 25, projectedMarginPct: 14.2, deviationPp: -10.8, worstCostType: 'Installation' }],
  },
  'active-projects': { total: 3, updatedAt: at, items: [
    { id: 'p1', number: 'PRJ-2026-0001', name: 'Hospital São Lucas', partyName: 'Hospital', status: 'InProgress', promisedDate: iso(-4), forecastDate: null, slackDays: null, risk: 'Late', delayDays: 4, contractedAmount: 480_000 },
    { id: 'p2', number: 'PRJ-2026-0002', name: 'Ed. Monte Castelo', partyName: 'Monte Castelo', status: 'Fabricated', promisedDate: iso(10), forecastDate: iso(17), slackDays: -7, risk: 'AtRisk', delayDays: null, contractedAmount: 320_000 },
    { id: 'p3', number: 'PRJ-2026-0003', name: 'Residencial Aurora', partyName: 'Aurora', status: 'Planned', promisedDate: iso(60), forecastDate: iso(40), slackDays: 20, risk: 'None', delayDays: null, contractedAmount: 210_000 },
  ] },
  'active-projects/margins': [
    { projectId: 'p1', budgetedMarginPct: 22, projectedMarginPct: 20.1, deviationPp: -1.9 }, { projectId: 'p2', budgetedMarginPct: 25, projectedMarginPct: 14.2, deviationPp: -10.8 },
    { projectId: 'p3', budgetedMarginPct: 24, projectedMarginPct: 24, deviationPp: 0 },
  ],
  'active-projects/received': [{ projectId: 'p1', received: 240_000, receivedPct: 50 }, { projectId: 'p2', received: 96_000, receivedPct: 30 }, { projectId: 'p3', received: 0, receivedPct: 0 }],
  production: {
    activeProjects: 4, openOrders: 12, lateOrders: 2, atRiskOrders: 3, busyUntil: iso(30), planComputedAt: at, updatedAt: at,
    weeks: [0, 1, 2, 3, 4].map((i) => ({ start: iso(7 * i), end: iso(7 * i + 6), isoWeek: 44 + i, committedHours: 100 - i * 10, capacityHours: 120, percent: Math.round((100 - i * 10) / 1.2), band: 'Normal' })),
  },
  backlog: { toProduce: 690_000, toProduceCount: 2, contracted: 1_010_000, recognized: 336_000, toInvoice: 674_000, toInvoiceCount: 3, provisional: true, updatedAt: at },
  deliveries: { year: 2026, month: 10, forecastInMonth: 2, deliveredInMonth: 1, atRisk: 1, late: 1, updatedAt: at,
    mostDelayed: { projectId: 'p2', number: 'PRJ-2026-0002', name: 'Ed. Monte Castelo', promisedDate: iso(10), forecastDate: iso(17), delayDays: 7 } },
}

const line = (key: string, label: string, kind: string, values: number[], drillable = true) => ({ key, label, kind, values, total: values.reduce((a, b) => a + b, 0), drillable })
const LINES = [
  line('revenue', 'Receita', 'Revenue', [30_000, 0, 45_500.5]),
  line('Material', '(−) Material', 'Cost', [8_200, -700.25, 0]), line('Labor', '(−) Mão de obra', 'Cost', [0, 3_150.75, 0]), line('Installation', '(−) Instalação', 'Cost', [0, 12_000, 0]),
  line('Freight', '(−) Frete', 'Cost', [0, 0, 980.4]), line('Tax', '(−) Impostos', 'Cost', [0, 0, 4_550.05]), line('Other', '(−) Outros', 'Cost', [0, 0, 0]),
  line('costs', 'Custos diretos', 'Costs', [8_200, 14_450.5, 5_530.45], false), line('result', '= Resultado', 'Result', [21_800, -14_450.5, 39_970.05], false),
]
const costs = (m: Record<string, number>) => ['Material', 'Labor', 'Installation', 'Freight', 'Tax', 'Other'].map((t) => ({ costType: t, amount: m[t] ?? 0 }))
const RESULTS = {
  from: MONTHS[0], to: m3, months: MONTHS.map((month) => ({ month, provisional: true })), provisional: true, page: 1, pageSize: 20, total: 2, updatedAt: at,
  totals: { projects: 2, revenue: 115_500.5, costs: costs({ Material: 7_499.75, Other: 41_250.1 }), directCosts: 69_931.05, result: 45_569.45, marginPct: 39.5 },
  items: [
    { projectId: 'r2', number: 'PRJ-2026-0009', name: 'Galpão Norte', partyId: 'c9', partyName: 'Norte', status: 'Delivered', revenue: 40_000, costs: costs({ Other: 41_250.1, Labor: 500 }), directCosts: 41_750.1, result: -1_750.1, marginPct: -4.4 },
    { projectId: 'r1', number: 'PRJ-2026-0001', name: 'Hospital São Lucas', partyId: 'c1', partyName: 'Hospital', status: 'InProgress', revenue: 75_500.5, costs: costs({ Material: 7_499.75 }), directCosts: 28_180.95, result: 47_319.55, marginPct: 62.7 },
  ],
}
const DETAIL = { projectId: 'r1', number: 'PRJ-2026-0001', name: 'Hospital São Lucas', partyName: 'Hospital', status: 'InProgress', from: MONTHS[0], to: m3, months: RESULTS.months, lines: LINES, marginPct: 62.7, updatedAt: at }

function api(cards: string[], withCost = true): Record<string, (r: Route) => Promise<void> | void> {
  const handlers: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/dashboard': (r) => json(r, overview(cards)),
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: withCost, version: '1', modules: allModules().map((m) => ({ ...m, actions: withCost ? ACTIONS : ['view'] })) }),
    'GET /api/v1/dashboard/project-results': (r) => json(r, RESULTS),
    'GET /api/v1/projects/margins/portfolio': (r) => json(r, { projectCount: 0, worstCostType: null, figures: { lines: [], total: { costType: 'Other', budgeted: 0, realized: 0, committed: 0, projected: 0, deviation: 0, deviationPct: null, includesBackfill: false }, revenue: 0, budgetedMargin: 0, budgetedMarginPct: null, realizedMargin: 0, realizedMarginPct: null, projectedMargin: 0, projectedMarginPct: null, marginDeviationPp: null, taxBasis: 'Budgeted', taxGuides: 0 } }),
    'GET /api/v1/projects/margins/ranking': (r) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    'GET /api/v1/dashboard/project-results/r1': (r) => json(r, DETAIL),
    'GET /api/v1/dashboard/project-results/r1/cell': (r) => {
      const url = new URL(r.request().url())
      return json(r, url.searchParams.get('line') === 'Material'
        ? { line: 'Material', label: 'Material', month: url.searchParams.get('month'), total: -700.25, entries: [{ kind: 'StockReturn', document: 'Devolução da OS-2026-0004', date: iso(-30), amount: -700.25, hours: null, note: null, link: null }] }
        : { line: 'revenue', label: 'Receita', month: url.searchParams.get('month'), total: 30_000, entries: [{ kind: 'Receivable', document: 'CR-2026-0010', date: iso(-60), amount: 30_000, hours: null, note: null, link: '/app/financeiro/receber/x1' }] })
    },
  }
  for (const [code, body] of Object.entries(CARDS)) handlers[`GET /api/v1/dashboard/cards/${code}`] = (r) => json(r, body)
  return handlers
}

test.describe('Painel do proprietário completo (DSH-01b)', () => {
  test('Proprietário vê margem, projetos, produção, carteira e entregas; drill-down da margem e da entrega em risco (CA 1, 2 e 4)', async ({ page }) => {
    await mockApi(page, api(OWNER))
    await page.goto('/app/painel')
    const main = page.getByRole('main')
    const margin = main.getByRole('region', { name: 'Margem dos projetos' })
    await expect(margin.getByText('19,4%')).toBeVisible()
    await expect(margin.getByText('−3,6 pp abaixo dos 23,0% orçados')).toBeVisible()
    await expect(margin.getByRole('link', { name: /Margem realizada: 21,3%/ })).toBeVisible()
    await expect(main.getByRole('link', { name: /Ed. Monte Castelo vai atrasar 7 dias/ }).first()).toHaveAttribute('href', '/app/projetos/p2?aba=cronograma')

    const projects = main.getByRole('region', { name: 'Projetos em andamento' })
    await expect(projects.getByRole('link', { name: /\+7 dias: ver o cronograma de PRJ-2026-0002/ })).toHaveAttribute('href', /\/app\/projetos\/p2\?aba=cronograma/)
    await expect(projects.getByText('Atrasado 4 dias')).toBeVisible()
    const backlog = main.getByRole('region', { name: 'Carteira' })
    await expect(backlog.getByText('Provisório')).toBeVisible()
    await expect(main.getByRole('region', { name: 'Produção' }).getByRole('img', { name: 'Ocupação da fábrica por semana' })).toBeVisible()
    await expect(main.getByRole('region', { name: 'Entregas do mês' }).getByText('Previstas no mês', { exact: true })).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    if (test.info().project.name === 'desktop') {
      await expect(projects.getByRole('columnheader', { name: /Margem/ })).toBeVisible()
      await expect(projects.getByRole('cell', { name: '50,0%' })).toBeVisible()
    }

    await margin.getByRole('button', { name: /Margem projetada: ver o Orçado × realizado/ }).click()
    await expect(page).toHaveURL(/\/app\/projetos\/margens/)
    await page.goBack()
    await main.getByRole('region', { name: 'Margem dos projetos' }).getByRole('link', { name: /Margem realizada/ }).click()
    await expect(page).toHaveURL(/situacao=concluido.*entregueDe=2026-01-01/)
  })

  test('resultado por projeto: lista, um projeto mês a mês e uma célula pelo teclado (CA 6 a 8)', async ({ page }) => {
    await mockApi(page, api(OWNER))
    await page.goto('/app/painel')
    await page.getByRole('main').getByRole('region', { name: 'Margem dos projetos' }).getByRole('link', { name: 'Resultado por projeto' }).click()
    await expect(page).toHaveURL(/\/app\/painel\/resultado-por-projeto/)
    const main = page.getByRole('main')
    await expect(main.getByRole('heading', { level: 1, name: 'Resultado por projeto' })).toBeVisible()
    await expect(main.getByRole('note')).toContainText('Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento.')
    await expect(main.getByRole('link', { name: 'Resultado negativo de PRJ-2026-0009: ver os custos' })).toHaveAttribute('href', /\/app\/projetos\/r2\?aba=custos/)
    await main.getByRole('link', { name: 'PRJ-2026-0001' }).click()
    await expect(page).toHaveURL(/resultado-por-projeto\/r1/)
    await expect(main.getByRole('img', { name: 'Receita × custos diretos por mês' })).toBeVisible()
    await expect(main.getByText(/Resultado do período/)).toBeVisible()

    const grid = main.getByRole('treegrid', { name: /Resultado de PRJ-2026-0001 mês a mês/ })
    const mobile = test.info().project.name === 'celular'
    // Linha Material (3ª linha de dados), coluna do mês passado (ou o mês mostrado no celular, com a seta para trás).
    if (mobile) await main.getByRole('button', { name: 'Mês anterior' }).click()
    const material = grid.getByRole('row', { name: /Material/ })
    await material.getByRole('gridcell').nth(mobile ? 1 : 2).focus()
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog', { name: 'Material' })
    await expect(panel.getByText('Devolução da OS-2026-0004')).toBeVisible()
    await expect(panel.getByText('Total da célula')).toBeVisible()
    await expect(panel.getByText('-R$ 700,25').or(panel.getByText('−R$ 700,25')).or(panel.getByText('R$ -700,25')).first()).toBeVisible()
  })

  test('Financeiro sem view_cost: sem card Margem, sem coluna de margem e sem resultado por projeto (CA 2 e 11)', async ({ page }) => {
    await mockApi(page, api(FINANCE, false))
    await page.goto('/app/painel')
    const main = page.getByRole('main')
    await expect(main.getByRole('region', { name: 'Projetos em andamento' })).toBeVisible()
    await expect(main.getByRole('region', { name: 'Margem dos projetos' })).toHaveCount(0)
    await expect(main.getByRole('region', { name: 'Projetos em andamento' }).getByRole('columnheader', { name: /Margem/ })).toHaveCount(0)
    await expect(main.getByRole('link', { name: 'Resultado por projeto' })).toHaveCount(0)
    await page.goto('/app/painel/resultado-por-projeto')
    await expect(page.getByRole('heading', { level: 1, name: 'Sem acesso a esta tela' })).toBeVisible()
  })

  test('a 400 px: os cards novos depois dos da DSH-01a, a tabela sem rolagem e o resultado um mês por vez', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await mockApi(page, api(OWNER))
    await page.goto('/app/painel')
    await expect(page.getByRole('main').getByRole('region', { name: 'Entregas do mês' })).toBeVisible()
    const tops = await page.evaluate(() => Array.from(document.querySelectorAll<HTMLElement>('[data-card]')).map((e) => ({ code: e.dataset.card, top: e.getBoundingClientRect().top })))
    expect([...tops].sort((a, b) => a.top - b.top).map((t) => t.code)).toEqual(['balances', 'revenue', 'overdue', 'margin', 'active-projects', 'production', 'backlog', 'deliveries'])
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
    const projects = page.getByRole('main').getByRole('region', { name: 'Projetos em andamento' })
    await expect(projects.getByRole('columnheader', { name: 'Contratado' })).toBeHidden()
    await projects.getByText('Valores').first().click()
    await expect(projects.getByText('R$ 480.000').first()).toBeVisible()

    await page.goto('/app/painel/resultado-por-projeto/r1')
    await expect(page.getByRole('main').getByRole('treegrid')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
    const next = await page.getByRole('button', { name: 'Mês anterior' }).boundingBox()
    expect(next?.height ?? 0).toBeGreaterThanOrEqual(44)
  })
})
