import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const withHr = (actions: string[]) => allModules().map((m) => (m.code === 'hr' ? { ...m, actions } : m))

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

const ITEMS = [
  { id: 'i-sal', code: 'SAL', name: 'Salário', kind: 'Earning', inssBase: true, fgtsBase: true, irrfBase: true, isActive: true, isStandard: true, confirmed: false, confirmedByName: null, confirmedAt: null, inUse: true, version: 1 },
  { id: 'i-he', code: 'HE50', name: 'Horas extras 50%', kind: 'Earning', inssBase: true, fgtsBase: true, irrfBase: true, isActive: true, isStandard: true, confirmed: false, confirmedByName: null, confirmedAt: null, inUse: false, version: 1 },
  { id: 'i-inss', code: 'INSS', name: 'INSS', kind: 'Deduction', inssBase: false, fgtsBase: false, irrfBase: true, isActive: true, isStandard: true, confirmed: false, confirmedByName: null, confirmedAt: null, inUse: false, version: 1 },
  { id: 'i-fgts', code: 'FGTS', name: 'FGTS', kind: 'Information', inssBase: false, fgtsBase: false, irrfBase: false, isActive: true, isStandard: true, confirmed: false, confirmedByName: null, confirmedAt: null, inUse: false, version: 1 },
]

/** Backend em memória da RH-02a (as regras reais estão nos testes de integração .NET). */
function payrollApi() {
  const state = {
    runs: [] as Record<string, unknown>[],
    slips: [
      { id: 's1', employeeId: 'e1', employeeName: 'Joana Lima', registration: '101', lines: [{ payrollItemId: 'i-sal', code: 'SAL', description: 'Salário', type: 'Earning', reference: '30 dias', amount: 3300 }], version: 1 },
      { id: 's2', employeeId: 'e2', employeeName: 'Rui Prado', registration: '102', lines: [{ payrollItemId: 'i-sal', code: 'SAL', description: 'Salário', type: 'Earning', reference: '30 dias', amount: 2800 }], version: 1 },
    ] as { id: string; employeeId: string; employeeName: string; registration: string; lines: { payrollItemId: string; code: string; description: string; type: string; reference: string | null; amount: number }[]; version: number }[],
    sensitiveCalls: [] as string[],
    idempotency: [] as (string | null)[],
  }
  const sum = (s: (typeof state.slips)[number]) => {
    const gross = s.lines.filter((l) => l.type === 'Earning').reduce((a, l) => a + l.amount, 0)
    const deductions = s.lines.filter((l) => l.type === 'Deduction').reduce((a, l) => a + l.amount, 0)
    return { gross, deductions, net: gross - deductions }
  }
  const row = (s: (typeof state.slips)[number]) => ({ id: s.id, employeeId: s.employeeId, employeeName: s.employeeName, registration: s.registration, ...sum(s), lineCount: s.lines.length, version: s.version })
  const detail = (s: (typeof state.slips)[number]) => ({ id: s.id, payrollRunId: 'r1', employeeId: s.employeeId, employeeName: s.employeeName, registration: s.registration, lines: s.lines, ...sum(s), inssBase: 0, fgtsBase: 0, irrfBase: 0, fgtsAmount: 0, editable: true, version: s.version })
  const sensitive = (r: Route, body: unknown) => { state.sensitiveCalls.push(new URL(r.request().url()).pathname); return json(r, body) }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/payroll-runs': (r) => json(r, state.runs),
    'GET /api/v1/hr/payroll-runs/sensitive': (r) => sensitive(r, state.runs.map((x) => ({ runId: x.id, gross: 6100, deductions: 0, net: 6100 }))),
    'POST /api/v1/hr/payroll-runs': (r) => {
      state.idempotency.push(r.request().headers()['idempotency-key'] ?? null)
      const b = r.request().postDataJSON()
      const run = { id: 'r1', period: b.period + '-01', type: b.type, mode: 'Entered', status: 'Draft', payslipCount: 2, createdAt: '2026-10-27T12:00:00Z', closedAt: null, closedByName: null, version: 1 }
      state.runs = [run]
      return json(r, run, 201)
    },
    'GET /api/v1/hr/payroll-runs/*': (r) => json(r, state.runs[0] ?? { id: 'r1', period: '2026-10-01', type: 'Monthly', mode: 'Entered', status: 'Draft', payslipCount: 2, createdAt: '2026-10-27T12:00:00Z', closedAt: null, closedByName: null, version: 1 }),
    'GET /api/v1/hr/payroll-runs/*/payslips/sensitive': (r) => sensitive(r, state.slips.map(row)),
    'GET /api/v1/hr/payroll-runs/*/payslips/*/sensitive': (r) => sensitive(r, detail(state.slips.find((s) => r.request().url().includes('/' + s.id + '/'))!)),
    'PUT /api/v1/hr/payroll-runs/*/payslips/*': (r) => {
      const b = r.request().postDataJSON()
      const s = state.slips.find((x) => r.request().url().endsWith('/' + x.id))!
      s.lines = b.lines.map((l: { payrollItemId: string; reference: string | null; amount: number }) => { const i = ITEMS.find((x) => x.id === l.payrollItemId)!; return { payrollItemId: i.id, code: i.code, description: i.name, type: i.kind, reference: l.reference, amount: l.amount } })
      s.version++
      return json(r, detail(s))
    },
    'POST /api/v1/hr/payroll-runs/*/import/preview': (r) => json(r, {
      employees: 2, employeeNames: ['Joana Lima', 'Rui Prado'], lines: 2, byItem: [{ code: 'HE50', name: 'Horas extras 50%', kind: 'Earning', lines: 2, total: 500 }], gross: 500, deductions: 0, net: 500, errors: [], errorCount: 0, canImport: true,
    }),
    'POST /api/v1/hr/payroll-runs/*/import': (r) => {
      for (const s of state.slips) s.lines = [...s.lines.filter((l) => l.code === 'SAL'), { payrollItemId: 'i-he', code: 'HE50', description: 'Horas extras 50%', type: 'Earning', reference: '10:00 h', amount: 250 }]
      return json(r, { lines: 2, employees: 2 })
    },
    'GET /api/v1/hr/payroll-runs/*/review/sensitive': (r) => sensitive(r, {
      payslipCount: 2, gross: 6600, deductions: 0, net: 6600, alertPct: 10, previousPeriod: null,
      byItem: [
        { payrollItemId: 'i-sal', code: 'SAL', name: 'Salário', kind: 'Earning', total: 6100, payslips: [{ payslipId: 's1', employeeName: 'Joana Lima', amount: 3300 }, { payslipId: 's2', employeeName: 'Rui Prado', amount: 2800 }] },
        { payrollItemId: 'i-he', code: 'HE50', name: 'Horas extras 50%', kind: 'Earning', total: 500, payslips: [{ payslipId: 's1', employeeName: 'Joana Lima', amount: 250 }, { payslipId: 's2', employeeName: 'Rui Prado', amount: 250 }] },
      ],
      variations: [], empty: [], newcomers: [{ payslipId: 's2', employeeId: 'e2', employeeName: 'Rui Prado', quarantineUntil: null }], accountChanged: [],
    }),
    'GET /api/v1/hr/payroll-runs/*/charges/sensitive': (r) => sensitive(r, { items: [], defaults: [], payslipsFgts: 0, fgtsCharges: null, fgtsMismatch: false, editable: true }),
    'GET /api/v1/hr/payroll-items': (r) => json(r, ITEMS),
    'GET /api/v1/audit/*/*': (r) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
  }
  return { state, api }
}

test.describe('RH-02a: folha lançada', () => {
  test('cria a folha, importa o CSV de exemplo e confere os totais (CA 4, 8, 9 e 11)', async ({ page }) => {
    const { state, api } = payrollApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: withHr(['view', 'create', 'edit', 'view_sensitive']) }) })
    await page.goto('/app/rh/folha')
    await expect(page.getByText('Nenhuma folha ainda.')).toBeVisible()
    await page.getByRole('button', { name: 'Nova folha' }).first().click()
    await page.getByLabel('Competência').fill('2026-10')
    await page.getByRole('button', { name: 'Criar folha', exact: true }).click()
    await expect(page).toHaveURL(/\/app\/rh\/folha\/r1/)
    await expect(page.getByText('Folha criada')).toBeVisible()
    expect(state.idempotency[0]).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByRole('button', { name: /Joana Lima/ })).toBeVisible()

    await page.getByRole('button', { name: 'Importar lançamentos' }).first().click()
    await page.getByLabel('Arquivo CSV').setInputFiles({ name: 'lancamentos.csv', mimeType: 'text/csv', buffer: Buffer.from('matricula_ou_cpf;codigo_rubrica;referencia;valor\n101;HE50;10:00 h;250,00\n102;HE50;10:00 h;250,00\n') })
    await expect(page.getByText('2 funcionários encontrados · 2 linhas')).toBeVisible()
    await expect(page.getByText('Substitui os lançamentos de 2 funcionários.', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Importar 2 lançamentos' }).click()
    await expect(page.getByText('2 lançamentos importados')).toBeVisible()

    await page.getByRole('button', { name: 'Conferir os totais' }).click()
    await expect(page).toHaveURL(/aba=conferencia/)
    await page.getByRole('button', { name: /HE50/ }).click()
    const drill = page.getByRole('list', { name: 'Holerites com Horas extras 50%' })
    await expect(drill.getByText('Total')).toBeVisible()
    await expect(drill).toContainText('R$ 500,00')
    await expect(page.getByRole('region', { name: 'Novos na folha' })).toContainText('Rui Prado')
    await noHorizontalScroll(page)
  })

  test('holerite no editor de linhas, pelo teclado, com o total ao vivo (CA 6)', async ({ page }) => {
    const { api } = payrollApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '2', modules: withHr(['view', 'create', 'edit', 'view_sensitive']) }) })
    await page.goto('/app/rh/folha/r1')
    await page.getByRole('button', { name: /Joana Lima/ }).click()
    const panel = page.getByRole('dialog')
    await expect(panel.getByText('Líquido: R$ 3.300,00')).toBeVisible()
    await panel.getByRole('button', { name: 'Adicionar linha' }).focus()
    await page.keyboard.press('Enter')
    await panel.getByLabel('Rubrica da linha 2').selectOption('i-inss')
    await panel.getByLabel('Valor da linha 2').fill('300,00')
    await expect(panel.getByText('Líquido: R$ 3.000,00')).toBeVisible()
    await panel.getByLabel('Rubrica da linha 2').selectOption('i-fgts')
    await expect(panel.getByText('Líquido: R$ 3.300,00')).toBeVisible()
    await panel.getByRole('button', { name: 'Salvar holerite' }).click()
    await expect(page.getByText('Holerite salvo')).toBeVisible()
    await noHorizontalScroll(page)
  })

  test('só com hr.view: nenhum valor pedido na rede e o bloco "Dados protegidos" (CA 12)', async ({ page }) => {
    const { state, api } = payrollApi()
    state.runs = [{ id: 'r1', period: '2026-10-01', type: 'Monthly', mode: 'Entered', status: 'Draft', payslipCount: 2, createdAt: '2026-10-27T12:00:00Z', closedAt: null, closedByName: null, version: 1 }]
    const urls: string[] = []
    page.on('request', (req) => urls.push(new URL(req.url()).pathname))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '3', modules: withHr(['view']) }) })
    await page.goto('/app/rh/folha')
    await expect(page.getByRole('link', { name: 'Mensal · outubro de 2026' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Nova folha' })).toHaveCount(0)
    await page.getByRole('link', { name: 'Mensal · outubro de 2026' }).click()
    await expect(page.getByRole('tabpanel').getByText('Dados protegidos')).toBeVisible()
    for (const tab of ['Encargos da empresa', 'Conferência']) {
      await page.getByRole('tab', { name: tab }).click()
      await expect(page.getByRole('tabpanel').getByText('Dados protegidos')).toBeVisible()
    }
    expect(urls.filter((u) => u.includes('/sensitive'))).toEqual([])
    expect(state.sensitiveCalls).toEqual([])
    await noHorizontalScroll(page)
  })
})
