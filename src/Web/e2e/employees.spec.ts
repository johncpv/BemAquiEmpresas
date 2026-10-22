import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const SCHEDULE = { id: 's44', name: '44 h — seg a sex 08:00–17:48', days: [], weeklyHours: 44, monthlyHours: 220, isActive: true, seedKey: '44h-seg-sex' }
const POSITION = { id: 'p1', name: 'Montador', cbo: null, defaultWorkCenterId: 'wc1', defaultWorkCenterName: 'Montagem', workScheduleId: 's44', workScheduleName: SCHEDULE.name, monthlyHours: 220, hourlyCost: 27, hourlyCostManual: false, isActive: true, createdAt: '2026-10-01T12:00:00Z' }
const EMPLOYEE = { id: 'e1', name: 'Joana Lima', registration: null, positionId: 'p1', positionName: 'Montador', workCenterId: 'wc1', workCenterName: 'Montagem', workScheduleId: 's44', unionId: null, admissionDate: '2026-10-19', terminationDate: null, status: 'Active', version: 3 }
const SENSITIVE = { id: 'e1', cpf: '52998224725', birthDate: '1990-05-10', baseSalary: 3300, rg: null, pis: null, ctpsNumber: null, ctpsSeries: null, voterId: null, zipCode: null, street: null, number: null, complement: null, district: null, city: null, state: null, phone: null, email: null }

/** Backend em memória da CAD-04b (as regras reais estão nos testes de integração .NET). */
function employeesApi(start: Record<string, unknown>[] = []) {
  const employees = [...start]
  const requests: string[] = []
  const posted: unknown[] = []
  const leaves: Record<string, unknown>[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/positions': (r) => json(r, { items: [POSITION], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/hr/positions/work-centers': (r) => json(r, [{ id: 'wc1', name: 'Montagem' }]),
    'GET /api/v1/hr/positions/*/sensitive': (r) => { requests.push(r.request().url()); return json(r, { id: 'p1', salaryMin: 3000, salaryMax: 3600, referenceSalary: 3300 }) },
    'GET /api/v1/hr/schedules': (r) => json(r, [SCHEDULE]),
    'GET /api/v1/hr/unions': (r) => json(r, []),
    'GET /api/v1/hr/employees': (r) => { requests.push(r.request().url()); return json(r, { items: employees, page: 1, pageSize: 20, total: employees.length }) },
    'POST /api/v1/hr/employees': (r) => { posted.push(r.request().postDataJSON()); employees.push(EMPLOYEE); return json(r, { employee: EMPLOYEE, warning: null }, 201) },
    'GET /api/v1/hr/employees/*': (r) => json(r, employees[0] ?? EMPLOYEE),
    'GET /api/v1/hr/employees/*/sensitive': (r) => { requests.push(r.request().url()); return json(r, SENSITIVE) },
    'GET /api/v1/hr/employees/*/timeline': (r) => json(r, []),
    'GET /api/v1/hr/employees/*/user': (r) => json(r, { user: null }),
    'GET /api/v1/hr/employees/*/leaves/sensitive': (r) => { requests.push(r.request().url()); return json(r, leaves) },
    'POST /api/v1/hr/employees/*/leaves': (r) => {
      const body = r.request().postDataJSON()
      posted.push(body)
      leaves.push({ id: 'l1', type: body.type, startDate: body.startDate, expectedEndDate: body.expectedEndDate, expectedReturnDate: '2031-01-01', reason: body.reason ?? '', userId: 'u1', createdAt: '2026-10-22T12:00:00Z' })
      employees[0] = { ...(employees[0] ?? EMPLOYEE), status: 'OnLeave', version: 4 }
      return json(r, employees[0])
    },
    'POST /api/v1/hr/employees/*/salary': (r) => { posted.push(r.request().postDataJSON()); return json(r, EMPLOYEE) },
  }
  return { api, requests, posted }
}

const withHr = (actions: string[]) => allModules().map((m) => (m.code === 'hr' ? { ...m, actions } : m))

async function press(page: Page, name: string, scope = page.locator('body')) {
  await scope.getByRole('button', { name, exact: true }).first().focus()
  await page.keyboard.press('Enter')
}

test.describe('Funcionários (CAD-04b)', () => {
  test('RH cadastra pelo teclado com salário sugerido pelo cargo e reajusta o salário (CA 1, 2 e 14)', async ({ page }) => {
    const { api, posted } = employeesApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '5', modules: withHr(['view', 'create', 'edit', 'delete', 'view_sensitive']) }) })
    await page.goto('/app/rh')
    await expect(page).toHaveURL(/\/app\/rh\/funcionarios$/)
    await expect(page.getByText('Cadastre os funcionários para apontar horas e preparar a folha.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo funcionário' }).first().focus()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/rh\/funcionarios\/novo$/)

    const main = page.getByRole('main')
    await main.getByRole('textbox', { name: /^Nome/ }).focus()
    await page.keyboard.type('Joana Lima')
    await main.getByRole('textbox', { name: /^CPF/ }).focus()
    await page.keyboard.type('52998224725')
    await main.getByRole('textbox', { name: /^Data de nascimento/ }).focus()
    await page.keyboard.type('10051990')
    await main.getByRole('combobox', { name: /^Cargo/ }).selectOption('p1')
    await expect(main.getByRole('textbox', { name: /^Salário base/ })).toHaveValue('3.300,00')
    await expect(main.getByRole('combobox', { name: 'Jornada' })).toHaveValue('s44')
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await press(page, 'Salvar funcionário', main)
    await expect(page.getByText('Funcionário salvo')).toBeVisible()
    expect(posted[0]).toMatchObject({ name: 'Joana Lima', cpf: '52998224725', birthDate: '1990-05-10', positionId: 'p1', workScheduleId: 's44', baseSalary: 3300 })
    await expect(page).toHaveURL(/\/app\/rh\/funcionarios\/e1/)
    await expect(page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Completar dados pessoais' })).toBeVisible()

    await page.getByRole('tab', { name: 'Contrato e salário' }).click()
    await expect(main.getByText(/R\$\s3\.300,00/)).toBeVisible()
    await press(page, 'Reajustar salário', main)
    const dialog = page.getByRole('alertdialog', { name: /Reajustar o salário de Joana Lima/ })
    await dialog.getByRole('textbox', { name: /^Novo salário/ }).focus()
    await page.keyboard.type('3500')
    await dialog.getByRole('textbox', { name: /^Motivo/ }).focus()
    await page.keyboard.type('Dissídio')
    await press(page, 'Reajustar salário', dialog)
    await expect(page.getByText('Salário reajustado')).toBeVisible()
    expect(posted[1]).toMatchObject({ newSalary: 3500, reason: 'Dissídio' })
  })

  test('com hr.view sem dados sensíveis, nada sensível é pedido à API (CA 3 e 8)', async ({ page }) => {
    const { api, requests } = employeesApi([EMPLOYEE])
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '6', modules: withHr(['view', 'create', 'edit']) }) })
    await page.goto('/app/rh/funcionarios')
    await expect(page.getByRole('main').getByText('Joana Lima').first()).toBeVisible()
    await expect(page.getByRole('link', { name: 'Novo funcionário' })).toHaveCount(0)
    await page.goto('/app/rh/funcionarios/e1?aba=pessoais')
    await expect(page.getByRole('tabpanel', { name: 'Dados pessoais' }).getByText('Dados protegidos')).toBeVisible()
    await page.getByRole('tab', { name: 'Contrato e salário' }).click()
    await expect(page.getByRole('tabpanel', { name: 'Contrato e salário' }).getByText('Dados protegidos')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Reajustar salário' })).toHaveCount(0)
    expect(requests.some((u) => u.includes('/sensitive'))).toBe(false)
  })

  test('RH registra afastamento em tela cheia a 400 px e a linha do tempo mostra a volta prevista (CAD-04c1 CA 1 e 5)', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const { api, posted } = employeesApi([EMPLOYEE])
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '7', modules: withHr(['view', 'create', 'edit', 'delete', 'view_sensitive']) }) })
    await page.goto('/app/rh/funcionarios/e1')
    const main = page.getByRole('main')
    await press(page, 'Registrar afastamento', main)
    const dialog = page.getByRole('alertdialog', { name: /Registrar afastamento de Joana Lima/ })
    await expect(dialog).toBeVisible()
    const box = await dialog.boundingBox()
    expect(box?.width).toBeGreaterThanOrEqual(399)
    await expect(dialog.getByText('Não informe CID nem diagnóstico.')).toBeVisible()
    await dialog.getByRole('combobox', { name: /^Tipo/ }).selectOption('Illness')
    await dialog.getByRole('textbox', { name: /^Fim previsto/ }).focus()
    await page.keyboard.type('31122030')
    await expect(dialog.getByText('Volta prevista em 01/01/2031.')).toBeVisible()
    await dialog.getByRole('textbox', { name: /^Motivo/ }).focus()
    await page.keyboard.type('Repouso')
    await press(page, 'Registrar afastamento', dialog)
    await expect(page.getByText('Afastamento registrado')).toBeVisible()
    expect(posted[0]).toMatchObject({ type: 'Illness', expectedEndDate: '2030-12-31', reason: 'Repouso', version: 3 })
    await expect(main.locator('[data-tone="warn"]', { hasText: 'Afastado' }).first()).toBeVisible()
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Ver na linha do tempo' }).click()
    const timeline = page.getByRole('tabpanel', { name: 'Linha do tempo' })
    await expect(timeline.getByText('Afastamento: Doença')).toBeVisible()
    await expect(timeline.getByText(/volta prevista em 01\/01\/2031/)).toBeVisible()
  })

  test('com hr.view, o afastado aparece só como "Afastado", sem tipo nem datas (CAD-04c1 CA 4)', async ({ page }) => {
    const { api, requests } = employeesApi([{ ...EMPLOYEE, status: 'OnLeave' }])
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '8', modules: withHr(['view']) }) })
    await page.goto('/app/rh/funcionarios')
    await expect(page.getByRole('main').locator('[data-tone="warn"]', { hasText: 'Afastado' }).first()).toBeVisible()
    await page.goto('/app/rh/funcionarios/e1?aba=linha-do-tempo')
    const timeline = page.getByRole('tabpanel', { name: 'Linha do tempo' })
    await expect(timeline.getByText('Dados protegidos')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Registrar afastamento' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Dica: Afastado' })).toBeVisible()
    expect(requests.some((u) => u.includes('/leaves/sensitive'))).toBe(false)
  })
})