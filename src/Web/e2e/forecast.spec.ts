import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']

const forecast = (over: Record<string, unknown> = {}) => ({
  forecastDate: '2026-11-25', fabricationEnd: '2026-11-18', reason: null, reasonOrderId: null, reasonOrderNumber: null, slackDays: 3, risk: 'None', delayDays: null,
  computedAt: '2026-10-30T13:00:00Z', installationOnly: false, ...over,
})
const project = (f: ReturnType<typeof forecast>) => ({
  id: 'p1', number: 'PRJ-2026-0003', name: 'Ed. Monte Castelo', status: 'InProgress', partyId: 'c1', partyName: 'Condomínio', contractId: 'k1', contractNumber: 'CTR-2026-0003',
  siteAddressId: null, siteAddress: null, promisedDate: '2026-11-28', forecastDate: f.forecastDate, priority: null, managerUserId: 'u1', managerName: 'Ana Souza',
  createdAt: '2026-09-01T12:00:00Z', items: [], version: 1, fabricatedOn: null, deliveredOn: null, deliveredManually: false, hasFabricationMilestone: true, contractStatus: 'Active',
  isOnboarding: false, fabricatedByOrders: false, fabricationOrders: 1, openFabricationOrders: 1, forecast: f,
})
const step = (i: number, code: string, name: string, status: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 0, awaitingApproval: false, selfApproved: false,
})
const order = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', partyId: 'c1', partyName: 'Condomínio',
  promisedDate: '2026-11-28', projectItemId: 'pi1', itemDescription: 'Cabina', productId: 'i1', productCode: 'CAB', productName: 'Cabina e portas', quantity: 1, priority: 0, routingTemplateId: 'rt1',
  routingTemplateName: 'Elevador', steps: [step(1, 'C', 'Corte', 'InProgress'), step(2, 'M', 'Montagem', 'Waiting')], totalStandardHours: 4, currentStepIndex: 0, started: true,
  delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 4,
}
const entry = {
  id: 'le1', stepId: 's1', stepCode: 'C', stepName: 'Corte', employeeId: 'e1', employeeName: 'Diego Santos', startedAt: '2026-10-30T11:00:00Z', endedAt: '2026-10-30T12:30:00Z',
  pausedMinutes: 0, netMinutes: 90, hours: 1.5, pauses: [], recordedByName: 'Ana Souza', onBehalf: true, closeKind: 'Supervisor', closedByName: 'Ana Souza', correctedAt: null,
  correctedByName: null, correctionReason: null, selfApproved: false, notes: null, isMine: false, version: 7,
}
const labor = { orderId: 'of1', orderNumber: 'OS-2026-00041', entries: [entry], totalHours: 1.5, awaitingApprovalStepId: null, awaitingApprovalStepName: null, version: 4 }

/** Backend simulado: o apontamento acima do padrão empurra a entrega prevista para depois do prometido (o recálculo real é dos testes .NET). */
function forecastApi() {
  let current = forecast()
  const sent: Record<string, unknown> = {}
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, projects: { actions: ALL } as never }) }),
    'GET /api/v1/projects/p1': (r) => json(r, project(current)),
    'GET /api/v1/projects': (r) => json(r, {
      items: [{ id: 'p1', number: 'PRJ-2026-0003', name: 'Ed. Monte Castelo', partyId: 'c1', partyName: 'Condomínio', status: 'InProgress', promisedDate: '2026-11-28', delayDays: null,
        contractValue: 100000, managerUserId: 'u1', managerName: 'Ana Souza', contractId: 'k1', forecastDate: current.forecastDate, slackDays: current.slackDays, risk: current.risk, forecastReason: current.reason }],
      page: 1, pageSize: 20, total: 1,
    }),
    'GET /api/v1/projects/margins': (r) => json(r, []),
    'GET /api/v1/projects/received': (r) => json(r, []),
    'GET /api/v1/projects/p1/costs': (r) => json(r, { lines: [], total: { costType: 'Other', budgeted: 0, realized: 0, committed: 0, projected: 0, deviation: 0, deviationPct: null, includesBackfill: false }, revenue: 100000, budgetedMarginPct: null, projectedMarginPct: null }),
    'GET /api/v1/projects/p1/financial': (r) => json(r, { receivables: [], payables: [], received: 0, toReceive: 0, paid: 0, toPay: 0 }),
    'GET /api/v1/contracts/k1': (r) => json(r, { id: 'k1', number: 'CTR-2026-0003', quoteId: 'q1', quoteNumber: 'ORC-2026-0009', totalValue: 100000, milestones: [], status: 'Active' }),
    'GET /api/v1/attachments/Project/p1': (r) => json(r, []),
    'GET /api/v1/attachments/ProductionOrder/of1': (r) => json(r, []),
    'GET /api/v1/quotes/lookups/customers/c1/addresses': (r) => json(r, []),
    'GET /api/v1/production/production-orders/of1': (r) => json(r, order),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r) => json(r, labor),
    'GET /api/v1/production/labor-entries/employees': (r) => json(r, [{ id: 'e2', name: 'Rafael Alves', registration: '0042', positionName: 'Montador' }]),
    'GET /api/v1/production/schedule/orders': (r) => json(r, [{ orderId: 'of1', plannedStart: '2026-10-30', plannedEnd: current.fabricationEnd, noForecastReason: null, steps: [] }]),
    'POST /api/v1/production/production-orders/of1/labor-entries': (r) => {
      sent.record = r.request().postDataJSON()
      current = forecast({ forecastDate: '2026-12-05', fabricationEnd: '2026-11-28', slackDays: -7, risk: 'AtRisk', computedAt: '2026-10-30T13:00:08Z' })
      return json(r, labor)
    },
  }
  return { api, sent, setForecast: (f: ReturnType<typeof forecast>) => { current = f } }
}

test.describe('Entrega prevista e risco (PLN-01b)', () => {
  test('apontamento acima do padrão muda a entrega prevista e o projeto fica Em risco', async ({ page }, info) => {
    const mobile = info.project.name === 'celular'
    const { api, sent } = forecastApi()
    await mockApi(page, api)

    await page.goto('/app/projetos/p1')
    await expect(page.getByText('25/11/2026')).toBeVisible()
    await expect(page.getByText('3 dias antes do prometido')).toBeVisible()
    await expect(page.locator('main').getByText('Em risco', { exact: true })).toHaveCount(0)

    await page.goto('/app/producao/os/of1?aba=apontamentos')
    await page.getByRole('button', { name: 'Apontar por funcionário' }).click()
    const record = page.getByRole('alertdialog')
    await record.getByLabel('Funcionário').selectOption('e2')
    await record.getByRole('button', { name: 'Apontar por funcionário' }).click()
    await expect(page.getByText('Apontamento registrado')).toBeVisible()
    expect(sent.record).toMatchObject({ employeeId: 'e2', stepId: 's1' })

    await page.goto('/app/projetos/p1')
    await expect(page.getByText('05/12/2026').first()).toBeVisible()
    await expect(page.getByText('7 dias depois do prometido')).toBeVisible()
    await expect(page.locator('main').getByText('Em risco', { exact: true })).toBeVisible()
    await expect(page.getByText('Vai atrasar 7 dias: previsão 05/12/2026, prometido ao cliente 28/11/2026.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver as OS do projeto' })).toHaveAttribute('href', /\/app\/producao\/os\?projeto=p1/)

    // Lista: a prevista e a folga abaixo da prometida (sem coluna nova a 400 px) e o chip âmbar.
    await page.goto('/app/projetos')
    await expect(page.getByText('Prevista 05/12/2026')).toBeVisible()
    await expect(page.getByText('7 dias depois do prometido')).toBeVisible()
    await expect(page.locator('main').getByText('Em risco', { exact: true })).toBeVisible()
    if (mobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  })

  test('sem previsão mostra o motivo e o botão que resolve', async ({ page }) => {
    const { api, setForecast } = forecastApi()
    setForecast(forecast({ forecastDate: null, fabricationEnd: null, slackDays: null, reason: 'NoRouting', reasonOrderId: 'of1', reasonOrderNumber: 'OS-2026-00041' }))
    await mockApi(page, api)

    await page.goto('/app/projetos/p1')
    await expect(page.getByText('Sem previsão: OS OS-2026-00041 sem roteiro')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Escolher roteiro' })).toHaveAttribute('href', '/app/producao/os/of1')
  })

  test('prazo de instalação em Parâmetros com "A confirmar" e conferência', async ({ page }) => {
    let production = { showLaborEntryCost: false, openLaborAlertHours: 12, showLaborEntryCostId: null, openLaborAlertHoursId: null, installationDays: 5, installationDaysConfirmed: false, installationDaysId: null as string | null }
    let body: unknown
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never }) }),
      'GET /api/v1/settings/parameters': (r: Route) => json(r, { payrollChargesPct: 80, pricingMode: 'Markup', chargesSettingId: null, pricingSettingId: null }),
      'GET /api/v1/production/parameters': (r: Route) => json(r, production),
      'PUT /api/v1/production/parameters': (r: Route) => {
        body = r.request().postDataJSON()
        production = { ...production, ...(body as object), installationDaysId: 'pp3' }
        return json(r, production)
      },
    })

    await page.goto('/app/configuracoes/parametros')
    const group = page.getByRole('group', { name: 'Prazo de instalação' })
    await expect(group.getByText('A confirmar', { exact: true })).toBeVisible()
    const days = group.getByRole('textbox', { name: /Prazo padrão de instalação/ })
    await expect(days).toHaveValue('5')
    await days.fill('121')
    await expect(page.getByText('Informe de 0 a 120 dias úteis.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Salvar parâmetros da produção' })).toBeDisabled()
    await days.fill('7')
    await group.getByRole('checkbox', { name: 'Prazo conferido com a empresa' }).check()
    await page.getByRole('button', { name: 'Salvar parâmetros da produção' }).click()
    await expect(page.getByText('Parâmetros da produção salvos. A entrega prevista dos projetos está sendo recalculada.')).toBeVisible()
    expect(body).toEqual({ showLaborEntryCost: false, openLaborAlertHours: 12, installationDays: 7, installationDaysConfirmed: true })
    await expect(group.getByText('A confirmar', { exact: true })).toHaveCount(0)
  })
})
