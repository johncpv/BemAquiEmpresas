import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, status: string, over: Record<string, unknown> = {}) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 0, awaitingApproval: false, selfApproved: false, ...over,
})
const order = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Monte Castelo', partyId: 'cli1', partyName: 'Condomínio',
  promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Cabina', productId: 'i1', productCode: 'CAB', productName: 'Cabina e portas', quantity: 1, priority: 0, routingTemplateId: 'rt1',
  routingTemplateName: 'Elevador', steps: [step(1, 'C', 'Corte', 'InProgress', { requiresApproval: true, appliedHours: 1.5 }), step(2, 'M', 'Montagem', 'Waiting')],
  totalStandardHours: 4, currentStepIndex: 0, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 4,
}
const entry = (over: Record<string, unknown> = {}) => ({
  id: 'le1', stepId: 's1', stepCode: 'C', stepName: 'Corte', employeeId: 'e1', employeeName: 'Diego Santos', startedAt: '2026-10-22T11:00:00Z', endedAt: '2026-10-22T12:30:00Z',
  pausedMinutes: 0, netMinutes: 90, hours: 1.5, pauses: [{ id: 'p1', reason: 'Material', notes: null, startedAt: '2026-10-22T11:30:00Z', endedAt: '2026-10-22T11:30:00Z' }], recordedByName: 'Ana Souza',
  onBehalf: true, closeKind: 'Supervisor', closedByName: 'Ana Souza', correctedAt: null, correctedByName: null, correctionReason: null, selfApproved: false, notes: null, isMine: false, version: 7, ...over,
})

test.describe('Apontamentos no backoffice (PRD-03a)', () => {
  test('aba Apontamentos: marcas, aprovar etapa, apontar por funcionário e corrigir', async ({ page }) => {
    let labor = { orderId: 'of1', orderNumber: 'OS-2026-00041', entries: [entry()], totalHours: 1.5, awaitingApprovalStepId: 's1', awaitingApprovalStepName: 'Corte', version: 4 }
    const sent: Record<string, unknown> = {}
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never }) }),
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
      'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, labor),
      'GET /api/v1/production/labor-entries/employees': (r: Route) => json(r, [{ id: 'e2', name: 'Rafael Alves', registration: '0042', positionName: 'Montador' }]),
      'POST /api/v1/production/production-orders/of1/steps/s1/approve': (r: Route) => { sent.approve = r.request().postDataJSON(); labor = { ...labor, awaitingApprovalStepId: null as never, awaitingApprovalStepName: null as never }; return json(r, labor) },
      'POST /api/v1/production/production-orders/of1/labor-entries': (r: Route) => { sent.record = r.request().postDataJSON(); sent.key = r.request().headers()['idempotency-key']; return json(r, labor) },
      'POST /api/v1/production/production-orders/of1/labor-entries/le1/correct': (r: Route) => { sent.correct = r.request().postDataJSON(); return json(r, labor) },
      'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    })

    await page.goto('/app/producao/os/of1?aba=apontamentos')
    await expect(page.getByText('Apontado por Ana Souza')).toBeVisible()
    await expect(page.getByText('A etapa Corte foi finalizada e aguarda aprovação.')).toBeVisible()
    await page.getByRole('button', { name: 'Aprovar etapa' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Aprovar etapa' }).click()
    await expect(page.getByText('Etapa aprovada')).toBeVisible()
    expect(sent.approve).toEqual({ version: 4 })

    await page.getByRole('button', { name: 'Apontar por funcionário' }).click()
    const record = page.getByRole('alertdialog')
    await record.getByLabel('Funcionário').selectOption('e2')
    await record.getByRole('button', { name: 'Apontar por funcionário' }).click()
    await expect(page.getByText('Apontamento registrado')).toBeVisible()
    expect(sent.record).toMatchObject({ employeeId: 'e2', stepId: 's1', endedAt: null, pauses: [] })
    expect(sent.key).toBeTruthy()

    await page.getByRole('button', { name: 'Corrigir apontamento' }).click()
    const fix = page.getByRole('alertdialog')
    await expect(fix.getByRole('button', { name: 'Corrigir apontamento' })).toBeDisabled()
    await fix.getByRole('textbox', { name: 'Motivo' }).fill('Esqueceu de finalizar no tablet.')
    await fix.getByRole('button', { name: 'Corrigir apontamento' }).click()
    await expect(page.getByText('Apontamento corrigido')).toBeVisible()
    expect(sent.correct).toMatchObject({ reason: 'Esqueceu de finalizar no tablet.', version: 7 })
  })

  test('apontamentos abertos levam direto a corrigir ou concluir; vazio explica', async ({ page }, info) => {
    let items = [{ id: 'le9', orderId: 'of1', orderNumber: 'OS-2026-00041', stepId: 's1', stepCode: 'C', stepName: 'Corte', employeeName: 'Diego Santos', startedAt: '2026-10-21T08:00:00Z',
      openHours: 14.5, lastPauseReason: 'EndOfShift', lastPauseAt: '2026-10-21T17:00:00Z', orderVersion: 4, version: 3 }]
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never }) }),
      'GET /api/v1/production/labor-entries/open': (r: Route) => json(r, { hours: 12, items }),
    })
    await page.goto('/app/producao/apontamentos-abertos')
    await expect(page.getByText('Diego Santos')).toBeVisible()
    if (info.project.name !== 'celular') await expect(page.getByText(/Fim do turno/)).toBeVisible()
    await expect(page.getByRole('link', { name: 'Corrigir apontamento' })).toHaveAttribute('href', /corrigir=le9/)
    await expect(page.getByRole('link', { name: 'Concluir etapa' })).toHaveAttribute('href', /concluir=s1/)
    items = []
    await page.reload()
    await expect(page.getByText('Nenhum apontamento aberto há mais de 12 h.')).toBeVisible()
  })
})