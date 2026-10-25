import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const month = (offset: number) => {
  const [y, m] = new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' }).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1 + offset, 1)).toISOString().slice(0, 10)
}
const MONTHS = [month(-2), month(-1), month(0)]

const row = (key: string, kind: string, label: string, values: number[], extra: Record<string, unknown> = {}) => ({
  key, kind, label, group: null, accountId: null, code: null, parentKey: null, level: 1, values, total: values.reduce((s, v) => s + v, 0), percentOfNetRevenue: null,
  delta: values[2] - values[1], deltaPercent: values[1] === 0 ? null : Math.round(((values[2] - values[1]) / Math.abs(values[1])) * 10000) / 100, criticalWhenNegative: false, hasChildren: false, ...extra,
})

const REPORT = {
  from: MONTHS[0], to: MONTHS[2], isProvisional: true, hasEntries: true, pendingParameters: 5, lastViewedAt: '2026-10-20T13:00:00Z',
  months: MONTHS.map((m, i) => ({ month: m, isProvisional: true, changed: i === 1 })),
  changes: { since: '2026-10-20T13:00:00Z', count: 2, months: [MONTHS[1]] },
  toClassify: [{ accountId: 'a39', code: '3.9', name: 'Despesas a classificar', total: 200 }],
  rows: [
    row('group:GrossRevenue', 'Group', 'Receita bruta', [7000, 5000, 6300], { group: 'GrossRevenue', hasChildren: true, percentOfNetRevenue: 103.57 }),
    row('account:a11', 'Account', 'Venda de equipamentos', [7000, 5000, 6300], { group: 'GrossRevenue', accountId: 'a11', code: '1.1', parentKey: 'group:GrossRevenue', level: 2 }),
    row('group:Deductions', 'Group', '(−) Deduções', [0, 0, 630], { group: 'Deductions' }),
    row('subtotal:NetRevenue', 'Subtotal', '= Receita líquida', [7000, 5000, 5670], { percentOfNetRevenue: 100 }),
    row('group:Costs', 'Group', '(−) Custos', [4200, 6500, 0], { group: 'Costs' }),
    row('subtotal:GrossProfit', 'Subtotal', '= Lucro bruto', [2800, -1500, 5670], { criticalWhenNegative: true, percentOfNetRevenue: 39.45 }),
    row('group:OperatingExpenses', 'Group', '(−) Despesas operacionais', [1500, 1700, 2500], { group: 'OperatingExpenses', hasChildren: true }),
    row('account:a39', 'Account', 'Despesas a classificar', [0, 200, 0], { group: 'OperatingExpenses', accountId: 'a39', code: '3.9', parentKey: 'group:OperatingExpenses', level: 2 }),
    row('subtotal:OperatingResult', 'Subtotal', '= Resultado operacional', [1300, -3200, 3170], { criticalWhenNegative: true }),
    row('group:FinancialResult', 'Group', '(±) Resultado financeiro', [-35, 65, -65], { group: 'FinancialResult' }),
    row('group:OtherResults', 'Group', '(±) Outros resultados', [0, 120, 0], { group: 'OtherResults' }),
    row('subtotal:PeriodResult', 'Subtotal', '= Resultado do período', [1265, -3015, 3105], { criticalWhenNegative: true }),
    row('memo:Advances', 'Memo', 'Adiantamentos de clientes (sinal ainda sem nota)', [3000, 1500, 1000], { total: 1000 }),
  ],
}

const CELL = {
  label: 'Receita bruta', from: MONTHS[0], to: MONTHS[0], total: 7000, truncated: false, hasProtected: false,
  items: [
    { kind: 'Receivable', id: 'r1', number: 'REC-2026-0001', counterparty: 'Edifício Aurora', description: null, date: MONTHS[0], amount: 7000, link: '/app/financeiro/receber/r1', note: null, count: null, protected: false },
  ],
}

function api(marks: string[]): Record<string, (r: Route) => Promise<void> | void> {
  return {
    'GET /api/v1/finance/dre': (r) => json(r, REPORT),
    'GET /api/v1/finance/dre/cells': (r) => json(r, CELL),
    'GET /api/v1/finance/dre/changes': (r) => json(r, { since: REPORT.lastViewedAt, count: 2, items: [
      { kind: 'Receivable', id: 'r2', number: 'REC-2026-0002', date: MONTHS[1], month: MONTHS[1], effect: 'Cancelado', amount: -500, at: '2026-10-22T12:00:00Z', link: '/app/financeiro/receber/r2' },
      { kind: 'Payable', id: 'p2', number: 'CP-2026-0009', date: MONTHS[1], month: MONTHS[1], effect: 'Lançado', amount: -90, at: '2026-10-22T13:00:00Z', link: '/app/financeiro/pagar/p2' },
    ] }),
    'POST /api/v1/finance/dre/view-mark': (r) => { marks.push(r.request().url()); return r.fulfill({ status: 204 }) },
  }
}

test.describe('DRE gerencial (FIN-08)', () => {
  test('chip Provisório, avisos, navegação por célula, drill-down e o título de origem (CA 3, 9, 10, 12 e 13)', async ({ page }, info) => {
    test.skip(info.project.name !== 'desktop')
    const marks: string[] = []
    await mockApi(page, api(marks))
    await page.goto('/app/financeiro/dre')
    const main = page.getByRole('main')
    await expect(main.getByRole('heading', { level: 1, name: 'Quanto a empresa vendeu, gastou e lucrou em cada mês.' })).toBeVisible()
    await expect(main.getByText('Provisório').first()).toBeVisible()
    await expect(main.getByText(/Mudou desde a sua última consulta em .*: 2 documentos em/)).toBeVisible()
    await expect(main.getByText('R$ 200,00 em contas a classificar neste período.')).toBeVisible()
    await expect(main.getByText('A confirmar com a contadora')).toBeVisible()
    await expect.poll(() => marks.length).toBe(1)
    const grid = main.getByRole('treegrid', { name: 'Demonstração de resultado' })
    await expect(grid.getByRole('columnheader', { name: 'Δ mês anterior' })).toBeVisible()
    const results = await new AxeBuilder({ page }).include('main').analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    // Teclado: na coluna de nomes, esquerda fecha e direita abre o grupo; direita de novo vai ao primeiro mês; Enter abre o drill-down.
    const revenue = grid.locator('[role="gridcell"][aria-colindex="1"]').filter({ hasText: 'Receita bruta' }).first()
    await revenue.focus()
    await page.keyboard.press('ArrowLeft')
    await expect(grid.getByText('Venda de equipamentos')).toHaveCount(0)
    await page.keyboard.press('ArrowRight')
    await expect(grid.getByText('Venda de equipamentos')).toBeVisible()
    await page.keyboard.press('ArrowRight')
    await expect(page.locator(':focus')).toHaveAttribute('aria-colindex', '2')
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog')
    await expect(panel.getByText('Total da célula')).toBeVisible()
    await expect(panel.getByText('Edifício Aurora')).toBeVisible()
    await panel.getByRole('link', { name: /REC-2026-0001/ }).click()
    await expect(page).toHaveURL(/\/app\/financeiro\/receber\/r1$/)
  })

  test('Ver o que mudou lista os documentos com o efeito', async ({ page }, info) => {
    test.skip(info.project.name !== 'desktop')
    await mockApi(page, api([]))
    await page.goto('/app/financeiro/dre')
    await page.getByRole('button', { name: 'Ver o que mudou' }).click()
    const panel = page.getByRole('dialog', { name: 'O que mudou' })
    await expect(panel.getByText('Cancelado')).toBeVisible()
    await expect(panel.getByRole('link', { name: /CP-2026-0009/ })).toBeVisible()
  })

  test('Configurações: contas do DRE a confirmar com a contadora, conferir e trocar (CA 16)', async ({ page }, info) => {
    test.skip(info.project.name !== 'desktop')
    const options = [{ id: 'a13', code: '1.3', name: 'Receitas financeiras' }, { id: 'a14', code: '1.4', name: 'Outras receitas' }]
    const item = (key: string, label: string, accountId: string | null, extra: Record<string, unknown> = {}) => ({
      key, label, description: 'Explicação de ' + label, accountId, accountCode: accountId === 'a14' ? '1.4' : '1.3', accountName: 'Conta', enabled: null, reviewState: 'ToConfirm',
      reviewedByName: null, reviewedAt: null, parameterId: 'p-' + key, options, ...extra,
    })
    let view = {
      pendingCount: 2,
      items: [
        item('finance.dre_interest_income_account_id', 'Juros e multa recebidos', 'a13'),
        item('finance.dre_down_payment_as_advance', 'Sinal como adiantamento até a nota', null, { enabled: true, options: [] }),
      ],
    }
    const bodies: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/settings/parameters': (r) => json(r, { payrollChargesPct: 80, pricingMode: 'Markup', chargesSettingId: null, pricingSettingId: null }),
      'GET /api/v1/finance/parameters/dre': (r) => json(r, view),
      'POST /api/v1/finance/parameters/dre/confirm': (r) => {
        view = { pendingCount: 1, items: view.items.map((i, n) => (n === 0 ? { ...i, reviewState: 'Confirmed', reviewedByName: 'Ana Souza', reviewedAt: '2026-10-24T13:00:00Z' } : i)) }
        return json(r, view)
      },
      'PUT /api/v1/finance/parameters/dre': (r) => {
        bodies.push(r.request().postDataJSON())
        view = { pendingCount: 2, items: view.items.map((i, n) => (n === 0 ? { ...i, accountId: 'a14', accountCode: '1.4', reviewState: 'ToConfirm', reviewedByName: null, reviewedAt: null } : i)) }
        return json(r, view)
      },
    })
    await page.goto('/app/configuracoes/parametros')
    const interest = page.getByRole('group', { name: 'Juros e multa recebidos' })
    await expect(interest.getByText('A confirmar com a contadora')).toBeVisible()
    await interest.getByRole('button', { name: 'Marcar como conferido' }).click()
    await expect(interest.getByText(/Conferido por Ana Souza/)).toBeVisible()
    await interest.getByRole('combobox').selectOption('a14')
    await page.getByRole('button', { name: 'Salvar contas do DRE' }).click()
    await expect(interest.getByText('A confirmar com a contadora')).toBeVisible()
    expect(bodies[0]).toMatchObject({ interestIncomeAccountId: 'a14', downPaymentAsAdvance: null })
  })

  test('a 400 px: um mês por vez, grupos fechados e drill-down em tela cheia', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await mockApi(page, api([]))
    await page.goto('/app/financeiro/dre')
    const main = page.getByRole('main')
    const select = main.getByRole('combobox', { name: 'Mês' })
    await expect(select).toBeVisible()
    const grid = main.getByRole('treegrid', { name: 'Demonstração de resultado' })
    await expect(grid.locator('thead th')).toHaveCount(4)
    await expect(grid.getByText('Venda de equipamentos')).toHaveCount(0)
    await select.selectOption({ index: 1 })
    await expect(grid.locator('[role="gridcell"][aria-colindex="2"]').first()).toContainText('5.000,00')
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    await grid.locator('[role="gridcell"][aria-colindex="2"]').first().click()
    const panel = page.getByRole('dialog')
    await expect(panel.getByText('Total da célula')).toBeVisible()
    const box = await panel.boundingBox()
    expect(box?.width ?? 0).toBeGreaterThanOrEqual(399)
  })
})
