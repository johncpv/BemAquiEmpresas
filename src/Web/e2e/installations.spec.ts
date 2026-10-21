import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const step = (sequence: number, name: string, done: boolean) => ({ id: `s${sequence}`, sequence, name, completedAt: done ? '2026-10-20T12:00:00Z' : null, completedByName: done ? 'Ana' : null })
const order = (accepted = false) => ({
  id: 'os1', number: 'OS-2026-00001', projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: 'Aurora — Elevador', contractId: 'k1', installerId: 'i1', installerName: 'Montagens Silva',
  contractValue: 5000, plannedStart: '2026-10-20', plannedEnd: '2026-10-25', status: accepted ? 'Completed' : 'InProgress', startedOn: '2026-10-20', acceptedOn: accepted ? '2026-10-20' : null,
  acceptedByName: accepted ? 'Carlos' : null, cancelReason: null, version: 3,
  steps: [step(1, 'Preparação da obra', true), step(2, 'Montagem na obra', true), step(3, 'Testes e ajustes', true), step(4, 'Entrega técnica', accepted)],
  checklist: [{ id: 'c1', sequence: 1, text: 'Área limpa', result: 'Done', markedAt: '2026-10-20T12:00:00Z', markedByName: 'Ana' }],
  items: [], payable: { id: 'cp1', number: 'CP-2026-0001', status: accepted ? 'Open' : 'Planned', amount: 5000, dueDate: '2026-10-25', isEstimate: !accepted },
})

test.describe('OS de instalação (COM-06)', () => {
  test('checklist com alvo de toque e aceite técnico com o impacto e os próximos passos', async ({ page }) => {
    let accepted = false
    let body: unknown
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, finance: { actions: ALL } as never }) }),
      'GET /api/v1/production/installations/os1': (r: Route) => json(r, order(accepted)),
      'GET /api/v1/contracts/k1': (r: Route) => json(r, { id: 'k1', number: 'CTR-2026-0001', milestones: [{ id: 'm2', description: 'Aceite', amount: 70000, triggerEvent: 'TechnicalAcceptance', daysAfterEvent: 10, receivable: { id: 'r2', number: 'CR-2026-0002', status: 'Planned', amount: 70000, balance: 70000 } }] }),
      'POST /api/v1/production/installations/os1/accept': (r: Route) => { body = r.request().postDataJSON(); accepted = true; return json(r, { payableId: 'cp1', payableNumber: 'CP-2026-0001', payableDueDate: '2026-10-30' }) },
      'GET /api/v1/attachments/ProductionOrder/os1': (r: Route) => json(r, []),
    })
    await page.goto('/app/producao/instalacoes/os1?aba=checklist')
    const done = page.getByRole('group', { name: 'Marcar Área limpa' }).getByRole('button', { name: 'Feito' })
    await expect(done).toHaveAttribute('aria-pressed', 'true')
    expect((await done.boundingBox())!.height).toBeGreaterThanOrEqual(56)
    await page.getByRole('button', { name: 'Registrar aceite técnico' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Libera a parcela Aceite', { exact: false })).toBeVisible()
    await dialog.getByRole('textbox').nth(1).fill('Carlos (síndico)')
    await dialog.getByRole('button', { name: 'Registrar aceite técnico' }).click()
    await expect(page.getByText('Aceite técnico registrado')).toBeVisible()
    expect(body).toMatchObject({ acceptedByName: 'Carlos (síndico)', version: 3 })
    await expect(page.getByRole('button', { name: 'Conferir título do instalador' })).toBeVisible()
  })

  test('lista vazia leva aos projetos', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/production/installations': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }) })
    await page.goto('/app/producao/instalacoes')
    await expect(page.getByText('Crie a OS de instalação a partir do projeto.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver projetos' })).toBeVisible()
  })
})
