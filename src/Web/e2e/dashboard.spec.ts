import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const iso = (offset: number) => new Date(Date.now() + offset * 86_400_000).toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
const at = new Date().toISOString()
const ALL = ['balances', 'cash-forecast', 'decisions', 'revenue', 'overdue', 'commercial']
const FINANCE = ['balances', 'cash-forecast', 'revenue', 'overdue', 'commercial']

const overview = (cards = ALL) => ({
  greeting: 'Boa tarde', firstName: 'Ana', today: iso(0), summary: 'Caixa positivo nas próximas 12 semanas; 2 decisões esperam você.', cards, generatedAt: at,
  attention: cards.includes('overdue') ? [{ code: 'overdue', tone: 'warn', title: 'Hospital São Lucas com título vencido', detail: 'R$ 74.670,00 vencidos há 9 dias. Atraso médio do cliente: 11 dias.', url: '/app/financeiro/receber/r1' }] : [],
})

const weeks = Array.from({ length: 12 }, (_, i) => ({ weekEnd: iso(7 * (i + 1)), baseBalance: 200_000 - i * 5000, adjustedBalance: 180_000 - i * 8000 }))
const CARDS: Record<string, unknown> = {
  balances: { date: iso(0), consolidated: 214_380, accounts: [{ id: 'b1', name: 'Inter', balance: 196_110 }, { id: 'b2', name: 'Caixa', balance: 18_270 }], openingMissing: false, updatedAt: at },
  'cash-forecast': {
    available: true, currentBalance: 214_380, weeks, horizons: [{ days: 30, baseBalance: 190_000, adjustedBalance: 170_000 }, { days: 60, baseBalance: 170_000, adjustedBalance: 140_000 }, { days: 90, baseBalance: 145_000, adjustedBalance: 92_000 }],
    lowestBase: { date: iso(84), balance: 145_000 }, lowestAdjusted: { date: iso(84), balance: 92_000 }, difference: 53_000, firstAdjustedNegative: null, updatedAt: at,
  },
  decisions: { available: true, total: 2, totalAmount: 38_420, firstDue: iso(4), updatedAt: at, items: [
    { kind: 'payment_order', title: 'OP-2026-0007 · Gama Aços', detail: 'Chapas de aço', amount: 30_000, dueDate: iso(4), url: '/app/financeiro/ordens-de-pagamento/op7' },
    { kind: 'payment_order', title: 'OP-2026-0008 · Frete', detail: null, amount: 8_420, dueDate: null, url: '/app/financeiro/ordens-de-pagamento/op8' },
  ] },
  revenue: { year: 2026, month: 10, available: true, monthAmount: 312_600, yearAmount: 2_900_000, monthTarget: null, yearTarget: null, monthPct: null, yearPct: null, provisional: false, workingDaysLeft: 5, canEditTargets: true, updatedAt: at },
  overdue: { today: iso(0), total: 82_870, count: 3, customers: 2, maxDaysLate: 21, updatedAt: at, largest: null,
    buckets: [{ label: '0–30', fromDays: 1, toDays: 30, total: 82_870, count: 3 }, { label: '31–60', fromDays: 31, toDays: 60, total: 0, count: 0 }, { label: '61–90', fromDays: 61, toDays: 90, total: 0, count: 0 }, { label: '90+', fromDays: 91, toDays: null, total: 0, count: 0 }],
    topCustomers: [{ partyId: 'c1', partyName: 'Hospital São Lucas', total: 74_670, maxDaysLate: 9, avgDelayDays: 11 }, { partyId: 'c2', partyName: 'Ed. Monte Castelo', total: 8_200, maxDaysLate: 21, avgDelayDays: null }] },
  commercial: { from: iso(-90), to: iso(0), openCount: 16, openAmount: 612_000, conversionByCount: 34, conversionByAmount: 30, decided: 29, expiringCount: 3, expiringAmount: 90_000, updatedAt: at, stages: [
    { status: 'Draft', situation: 'rascunho', count: 3, amount: 50_000 }, { status: 'Sent', situation: 'enviado', count: 13, amount: 562_000 },
    { status: 'Approved', situation: 'aprovado', count: 10, amount: 400_000 }, { status: 'Rejected', situation: 'perdido', count: 7, amount: 120_000 }, { status: 'Expired', situation: 'expirado', count: 2, amount: 30_000 },
  ] },
}

function dashboardApi(cards = ALL, change: Record<string, (r: Route) => Promise<void> | void> = {}) {
  const handlers: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/dashboard': (r) => json(r, overview(cards)),
    'POST /api/v1/dashboard/refresh': (r) => r.fulfill({ status: 204 }),
    'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
  }
  for (const code of ALL) handlers[`GET /api/v1/dashboard/cards/${code}`] = (r) => json(r, CARDS[code])
  return { ...handlers, ...change }
}

const permissions = (isOwner: boolean, approve = isOwner) => (r: Route) => json(r, {
  permissions: [], isOwner, version: '1',
  modules: allModules().map((m) => (approve ? m : { ...m, actions: ['view'] })),
})

async function login(page: Page) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill('ana@konebras.com.br')
  await page.getByLabel('Senha').fill('uma frase longa de teste')
  await page.getByRole('button', { name: 'Entrar' }).click()
}

test.describe('Painel do proprietário (DSH-01a)', () => {
  test('Proprietário entra e cai no painel, troca o cenário, vai à origem de um número e volta (CA 2, 3, 7 e 10)', async ({ page }) => {
    await mockApi(page, { ...dashboardApi(), 'POST /api/v1/auth/login': (r) => json(r, { accessToken: 'tok-dono', expiresAt: '2030-01-01T00:00:00Z' }), 'GET /api/v1/me/permissions': permissions(true) }, { session: false })
    await login(page)
    await expect(page).toHaveURL(/\/app\/painel$/)
    const main = page.getByRole('main')
    await expect(main.getByRole('heading', { level: 1, name: 'Boa tarde, Ana' })).toBeVisible()
    await expect(main.getByText('Caixa positivo nas próximas 12 semanas; 2 decisões esperam você.', { exact: false })).toBeVisible()
    await expect(main.getByRole('link', { name: /Hospital São Lucas com título vencido/ })).toHaveAttribute('href', '/app/financeiro/receber/r1')
    await expect(main.getByRole('region', { name: /Aguardando sua decisão/ }).getByRole('link', { name: /OP-2026-0007/ })).toBeVisible()
    await expect(main.getByRole('region', { name: /Faturado em/ }).getByRole('link', { name: 'Definir meta' })).toBeVisible()
    await expect(main.getByText(/Atualizado às \d\d:\d\d/).first()).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    const cash = main.getByRole('region', { name: /Caixa projetado/ })
    await cash.getByRole('radio', { name: 'Base' }).click()
    await expect(page).toHaveURL(/cenario=base/)
    await cash.getByRole('button', { name: 'Ver origem' }).click()
    await expect(page).toHaveURL(/fluxo-de-caixa.*cenario=base/)
    await page.goBack()
    await expect(page).toHaveURL(/painel.*cenario=base/)

    await main.getByRole('button', { name: /Saldo em contas: ver origem/ }).click()
    await expect(page).toHaveURL(/\/app\/financeiro\/contas/)
    await page.goBack()
    await expect(page).toHaveURL(/\/app\/painel/)
    await expect(page.getByRole('heading', { level: 1, name: 'Boa tarde, Ana' })).toBeVisible()
  })

  test('Financeiro entra no Início e vê no painel só os cards dele (CA 3 e 5)', async ({ page }) => {
    await mockApi(page, { ...dashboardApi(FINANCE), 'POST /api/v1/auth/login': (r) => json(r, { accessToken: 'tok-fin', expiresAt: '2030-01-01T00:00:00Z' }), 'GET /api/v1/me/permissions': permissions(false, false) }, { session: false })
    await login(page)
    await expect(page).toHaveURL(/\/app$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Início' })).toBeVisible()
    await page.getByRole('link', { name: 'Painel do proprietário' }).first().click()
    await expect(page).toHaveURL(/\/app\/painel/)
    const main = page.getByRole('main')
    await expect(main.getByRole('region', { name: /Saldo em contas/ })).toBeVisible()
    await expect(main.getByRole('region', { name: /Comercial/ })).toBeVisible()
    await expect(main.getByRole('region', { name: /Aguardando sua decisão/ })).toHaveCount(0)
  })

  test('card com erro não derruba os outros; Atualizar bloqueado mostra o aviso; sem card, a mensagem (CA 4, 11 e 13)', async ({ page }) => {
    let failing = true
    await mockApi(page, dashboardApi(ALL, {
      'GET /api/v1/dashboard/cards/commercial': (r) => (failing ? json(r, { title: 'Erro' }, 500) : json(r, CARDS.commercial)),
      'POST /api/v1/dashboard/refresh': (r) => json(r, { title: 'Aguarde alguns segundos para atualizar de novo.', detail: 'Aguarde alguns segundos para atualizar de novo.', code: 'dashboard_refresh_rate_limited' }, 429),
    }))
    await page.goto('/app/painel')
    const main = page.getByRole('main')
    await expect(main.getByText('Não foi possível carregar comercial.')).toBeVisible()
    await expect(main.getByRole('region', { name: /Saldo em contas/ }).getByRole('button', { name: /Saldo em contas: ver origem/ })).toBeVisible()
    failing = false
    await main.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(main.getByRole('region', { name: /Comercial/ }).getByText('Em aberto', { exact: true })).toBeVisible()
    await main.getByRole('button', { name: 'Atualizar' }).click()
    await expect(main.getByText('Aguarde alguns segundos para atualizar de novo.')).toBeVisible()

    await page.unrouteAll()
    await mockApi(page, dashboardApi([]))
    await page.goto('/app/painel')
    await expect(page.getByText('Você ainda não tem acesso aos indicadores. Fale com o administrador.')).toBeVisible()
  })

  test('metas de faturamento: "Repetir para o ano" e Salvar metas gravam os 12 meses (CA 9)', async ({ page }) => {
    const year = new Date().getFullYear()
    let body: { months: (number | null)[] } | undefined
    const empty = { year, months: Array.from({ length: 12 }, (_, i) => ({ month: i + 1, amount: null })), yearTotal: null }
    await mockApi(page, {
      'GET /api/v1/settings/parameters': (r) => json(r, { payrollChargesPct: 80, pricingMode: 'Markup', chargesSettingId: null, pricingSettingId: null }),
      [`GET /api/v1/finance/revenue-targets/${year}`]: (r) => json(r, empty),
      [`PUT /api/v1/finance/revenue-targets/${year}`]: (r) => {
        body = r.request().postDataJSON() as { months: (number | null)[] }
        return json(r, { year, months: body.months.map((amount, i) => ({ month: i + 1, amount })), yearTotal: 4_560_000 })
      },
    })
    await page.goto('/app/configuracoes/parametros#metas-de-faturamento')
    const section = page.getByRole('group', { name: 'Metas de faturamento' })
    await section.getByLabel('Janeiro').fill('380000')
    await section.getByRole('button', { name: 'Repetir para o ano' }).click()
    await expect(section.getByText('Total do ano:')).toContainText('4.560.000,00')
    await section.getByRole('button', { name: 'Salvar metas' }).click()
    await expect(page.getByText('Metas salvas')).toBeVisible()
    expect(body?.months).toEqual(Array(12).fill(380000))
  })

  test('módulo não liberado mostra a tela padrão (CA 16)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ dashboard: { released: false } }) }) })
    await page.goto('/app/painel')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })

  test('a 400 px: KPIs em 1 coluna, ordem Caixa, Decisões, Faturamento, Vencido e Comercial, nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await mockApi(page, dashboardApi())
    await page.goto('/app/painel')
    const main = page.getByRole('main')
    await expect(main.getByRole('region', { name: /Comercial/ }).getByText('Em aberto', { exact: true })).toBeVisible()
    const tops = await page.evaluate(() => Array.from(document.querySelectorAll<HTMLElement>('[data-card]')).map((e) => ({ code: e.dataset.card, top: e.getBoundingClientRect().top, left: e.getBoundingClientRect().left })))
    expect([...tops].sort((a, b) => a.top - b.top).map((t) => t.code)).toEqual(['balances', 'cash-forecast', 'decisions', 'revenue', 'overdue', 'commercial'])
    expect(new Set(tops.map((t) => Math.round(t.left))).size).toBe(1)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    const refresh = await main.getByRole('button', { name: 'Atualizar' }).boundingBox()
    expect(refresh?.height ?? 0).toBeGreaterThanOrEqual(44)
  })
})
