import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const SCHEDULE = { id: 's44', name: '44 h — seg a sex 08:00–17:48', days: [], weeklyHours: 44, monthlyHours: 220, isActive: true, seedKey: '44h-seg-sex' }
const POSITION = { id: 'p1', name: 'Montador', cbo: null, defaultWorkCenterId: 'wc1', defaultWorkCenterName: 'Montagem', workScheduleId: 's44', workScheduleName: SCHEDULE.name, monthlyHours: 220, hourlyCost: 27, hourlyCostManual: false, isActive: true, createdAt: '2026-10-01T12:00:00Z' }
const EMPLOYEE = { id: 'e1', name: 'Joana Lima', registration: null, positionId: 'p1', positionName: 'Montador', workCenterId: 'wc1', workCenterName: 'Montagem', workScheduleId: 's44', unionId: null, admissionDate: '2026-10-19', terminationDate: null, status: 'Active', version: 3 }
const SENSITIVE = { id: 'e1', cpf: '52998224725', birthDate: '1990-05-10', baseSalary: 3300, rg: null, pis: null, ctpsNumber: null, ctpsSeries: null, voterId: null, zipCode: null, street: null, number: null, complement: null, district: null, city: null, state: null, phone: null, email: null }

/** Afastamento como o /leaves/sensitive devolve (CAD-04c1b): estado calculado e rastro. */
const leaveRow = (patch: Record<string, unknown>) => ({
  id: 'l1', type: 'JustifiedAbsence', healthProtected: false, state: 'Current', startDate: '2020-01-01', expectedEndDate: '2098-12-31', expectedReturnDate: '2099-01-01',
  reason: null, userId: 'u1', createdAt: '2026-10-22T12:00:00Z', cancelledAt: null, changes: [], ...patch,
})

/** Backend em memória da CAD-04b (as regras reais estão nos testes de integração .NET). */
function employeesApi(start: Record<string, unknown>[] = [], startLeaves: Record<string, unknown>[] = []) {
  const employees = [...start]
  const requests: string[] = []
  const posted: unknown[] = []
  const leaves: Record<string, unknown>[] = [...startLeaves]
  const leaveOf = (r: Route) => leaves.find((l) => r.request().url().includes('/leaves/' + String(l.id)))!
  const change = (kind: string, l: Record<string, unknown>, after: Record<string, unknown>, reason: string | null = null) => ({
    id: 'c' + Math.random(), kind, oldStartDate: l.startDate, oldExpectedEndDate: l.expectedEndDate, newStartDate: after.startDate ?? l.startDate,
    newExpectedEndDate: after.expectedEndDate ?? l.expectedEndDate, oldType: l.type, newType: after.type ?? l.type, reason, userId: 'u1', createdAt: '2026-10-22T13:00:00Z',
  })
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
      leaves.push(leaveRow({ type: body.type, startDate: body.startDate, expectedEndDate: body.expectedEndDate, expectedReturnDate: '2031-01-01', reason: body.reason ?? '' }))
      employees[0] = { ...(employees[0] ?? EMPLOYEE), status: 'OnLeave', version: 4 }
      return json(r, employees[0])
    },
    'PUT /api/v1/hr/employees/*/leaves/*': (r) => {
      const body = r.request().postDataJSON()
      posted.push(body)
      const l = leaveOf(r)
      const after = { type: body.type, startDate: body.startDate, expectedEndDate: body.expectedEndDate, reason: body.reason }
      Object.assign(l, after, { changes: [change('Edited', l, after), ...(l.changes as unknown[])] })
      return json(r, employees[0])
    },
    'POST /api/v1/hr/employees/*/leaves/*/end': (r) => {
      const body = r.request().postDataJSON()
      posted.push(body)
      const l = leaveOf(r)
      const after = { expectedEndDate: '2026-10-21' }
      Object.assign(l, after, { state: 'Ended', changes: [change('EndedEarly', l, after), ...(l.changes as unknown[])] })
      employees[0] = { ...(employees[0] ?? EMPLOYEE), status: 'Active', version: 6 }
      return json(r, employees[0])
    },
    'POST /api/v1/hr/employees/*/leaves/*/cancel': (r) => {
      const body = r.request().postDataJSON()
      posted.push(body)
      const l = leaveOf(r)
      Object.assign(l, { state: 'Cancelled', cancelledAt: '2026-10-22T14:00:00Z', changes: [change('Cancelled', l, {}, body.reason), ...(l.changes as unknown[])] })
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
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '7', modules: withHr(['view', 'create', 'edit', 'delete', 'view_sensitive', 'view_health']) }) })
    await page.goto('/app/rh/funcionarios/e1')
    const main = page.getByRole('main')
    await press(page, 'Registrar afastamento', main)
    const dialog = page.getByRole('alertdialog', { name: /Registrar afastamento de Joana Lima/ })
    await expect(dialog).toBeVisible()
    const box = await dialog.boundingBox()
    expect(box?.width).toBeGreaterThanOrEqual(399)
    await expect(dialog.getByText('Não informe CID nem diagnóstico.')).toBeVisible()
    await dialog.getByRole('combobox', { name: /^Tipo/ }).selectOption('MedicalCertificate')
    await dialog.getByRole('textbox', { name: /^Fim previsto/ }).focus()
    await page.keyboard.type('31122030')
    await expect(dialog.getByText('Volta prevista em 01/01/2031.')).toBeVisible()
    await dialog.getByRole('textbox', { name: /^Motivo/ }).focus()
    await page.keyboard.type('Repouso')
    await press(page, 'Registrar afastamento', dialog)
    await expect(page.getByText('Afastamento registrado')).toBeVisible()
    expect(posted[0]).toMatchObject({ type: 'MedicalCertificate', expectedEndDate: '2030-12-31', reason: 'Repouso', version: 3 })
    await expect(main.locator('[data-tone="warn"]', { hasText: 'Afastado' }).first()).toBeVisible()
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Ver na linha do tempo' }).click()
    const timeline = page.getByRole('tabpanel', { name: 'Linha do tempo' })
    await expect(timeline.getByText('Afastamento: Atestado médico')).toBeVisible()
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

test.describe('Afastamentos: editar, encerrar e cancelar (CAD-04c1b)', () => {
  const TODAY_BR = () => new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const TODAY_ISO = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())

  test('RH edita o previsto, encerra o em curso com a volta em hoje e cancela, a 400 px (CA 1, 4, 6 e 9)', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const current = leaveRow({ id: 'l1' })
    const scheduled = leaveRow({ id: 'l2', type: 'JustifiedAbsence', state: 'Scheduled', startDate: '2099-01-10', expectedEndDate: '2099-01-20', expectedReturnDate: '2099-01-21' })
    const { api, posted } = employeesApi([{ ...EMPLOYEE, status: 'OnLeave', version: 5 }], [scheduled, current])
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '9', modules: withHr(['view', 'edit', 'view_sensitive', 'view_health']) }) })
    await page.goto('/app/rh/funcionarios/e1?aba=linha-do-tempo')
    const timeline = page.getByRole('tabpanel', { name: 'Linha do tempo' })
    const item = (text: string) => timeline.getByRole('listitem').filter({ hasText: text }).first()
    await expect(item('10/01/2099').getByText('Previsto')).toBeVisible()
    await expect(item('10/01/2099').getByRole('button', { name: 'Encerrar afastamento' })).toHaveCount(0)

    // Editar o previsto: o diálogo vem com o gravado e mostra o antes e o depois.
    await press(page, 'Editar', item('10/01/2099'))
    const edit = page.getByRole('alertdialog', { name: /Editar afastamento de Joana Lima/ })
    expect((await edit.boundingBox())?.width).toBeGreaterThanOrEqual(399)
    await expect(edit.getByRole('textbox', { name: /^Início/ })).toHaveValue('10/01/2099')
    await edit.getByRole('combobox', { name: /^Tipo/ }).selectOption('Military')
    await expect(edit.getByRole('group', { name: 'Impacto' })).toContainText('Tipo: Falta justificada → Serviço militar')
    await press(page, 'Salvar afastamento', edit)
    await expect(page.getByText('Afastamento salvo')).toBeVisible()
    expect(posted.at(-1)).toMatchObject({ type: 'Military', startDate: '2099-01-10', expectedEndDate: '2099-01-20', version: 5 })
    await expect(item('10/01/2099').getByText(/Editado em .*Falta justificada → Serviço militar/)).toBeVisible()

    // Encerrar o em curso: a volta vem preenchida com hoje.
    await press(page, 'Encerrar afastamento', item('01/01/2020'))
    const end = page.getByRole('alertdialog', { name: /Encerrar o afastamento de Joana Lima/ })
    expect((await end.boundingBox())?.width).toBeGreaterThanOrEqual(399)
    await expect(end.getByRole('textbox', { name: /^Data da volta/ })).toHaveValue(TODAY_BR())
    await expect(end.getByRole('group', { name: 'Impacto' })).toContainText('Fim previsto: 31/12/2098 →')
    await press(page, 'Encerrar afastamento', end)
    await expect(page.getByText('Afastamento encerrado')).toBeVisible()
    expect(posted.at(-1)).toMatchObject({ returnDate: TODAY_ISO(), version: 5 })
    await expect(item('01/01/2020').getByText('Encerrado', { exact: true })).toBeVisible()

    // Cancelar o previsto exige motivo.
    await press(page, 'Cancelar afastamento', item('10/01/2099'))
    const cancel = page.getByRole('alertdialog', { name: /Cancelar o afastamento de Joana Lima/ })
    await press(page, 'Cancelar afastamento', cancel)
    await expect(cancel.getByRole('alert')).toContainText('Informe o motivo do cancelamento.')
    await cancel.getByRole('textbox', { name: /^Motivo do cancelamento/ }).fill('Lançado em duplicidade')
    await press(page, 'Cancelar afastamento', cancel)
    await expect(page.getByText('Afastamento cancelado')).toBeVisible()
    expect(posted.at(-1)).toMatchObject({ reason: 'Lançado em duplicidade' })
    await expect(item('10/01/2099').getByText(/Cancelado em .*Lançado em duplicidade/)).toBeVisible()
    await expect(item('10/01/2099').getByRole('button', { name: 'Editar' })).toHaveCount(0)
    const results = await new AxeBuilder({ page }).include('main').analyze()
    expect(results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')).toEqual([])
  })

  test('sem hr.view_health, nenhum afastamento mostra o tipo, só "Afastado" e as datas, e os tipos de saúde somem do diálogo (CA 8)', async ({ page }) => {
    const health = leaveRow({ id: 'l1', type: null, healthProtected: true })
    const other = leaveRow({ id: 'l2', type: null, healthProtected: true, state: 'Scheduled', startDate: '2099-01-10', expectedEndDate: '2099-01-20', expectedReturnDate: '2099-01-21' })
    const { api } = employeesApi([{ ...EMPLOYEE, status: 'OnLeave' }], [other, health])
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '10', modules: withHr(['view', 'edit', 'view_sensitive']) }) })
    await page.goto('/app/rh/funcionarios/e1?aba=linha-do-tempo')
    const timeline = page.getByRole('tabpanel', { name: 'Linha do tempo' })
    const row = timeline.getByRole('listitem').filter({ hasText: '01/01/2020' }).first()
    await expect(row.getByText('Afastado', { exact: true })).toBeVisible()
    await expect(row.getByRole('button', { name: 'Dica: Dados de saúde' })).toBeVisible()
    await expect(row.getByRole('button', { name: 'Editar' })).toHaveCount(0)
    await expect(row.getByRole('button', { name: 'Encerrar afastamento' })).toBeVisible()
    const scheduled = timeline.getByRole('listitem').filter({ hasText: '10/01/2099' }).first()
    await expect(scheduled.getByText('Afastado', { exact: true })).toBeVisible()
    await expect(scheduled.getByRole('button', { name: 'Cancelar afastamento' })).toBeVisible()
    await expect(timeline.getByText(/Afastamento: /)).toHaveCount(0)

    await press(page, 'Registrar afastamento', page.getByRole('main'))
    const dialog = page.getByRole('alertdialog', { name: /Registrar afastamento de Joana Lima/ })
    const options = await dialog.getByRole('combobox', { name: /^Tipo/ }).locator('option').allTextContents()
    expect(options).toEqual(['Escolha', 'Maternidade', 'Paternidade', 'Serviço militar', 'Falta justificada'])
  })
})