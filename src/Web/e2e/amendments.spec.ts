import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const detail = (status: string) => ({
  id: 'a1', contractId: 'k1', contractNumber: 'CTR-2026-0001', number: 1, description: 'Mais uma parada', valueDelta: 10000, daysDelta: 15, status,
  milestones: [{ sequence: 1, description: 'Sinal do aditivo', percent: 100, amount: 10000, trigger: 'Event', triggerEvent: 'Signature', dueDate: null, daysAfterEvent: 0 }], reductions: [],
  preview: { valueBefore: 100000, valueAfter: 110000, promisedBefore: '2027-01-18', promisedAfter: '2027-02-02' }, budgetDeltas: { Material: 10000 },
  isAuthor: false, canDecide: status === 'PendingApproval', selfApproved: false, rejectionComment: null, approvedAt: null, approvedByName: null, createdByName: 'Ana', version: 2,
})

test.describe('Aditivo do contrato (COM-07)', () => {
  test('registra o acréscimo com marcos e aprova com o impacto', async ({ page }) => {
    let body: unknown
    let status = 'PendingApproval'
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ projects: { actions: ALL } as never }) }),
      'GET /api/v1/contracts/k1/amendments/context': (r: Route) => json(r, { currentValue: 100000, promisedDate: '2027-01-18', reducible: [], budget: { Material: 50000 } }),
      'POST /api/v1/contracts/k1/amendments': (r: Route) => { body = r.request().postDataJSON(); return json(r, detail('Draft'), 201) },
      'GET /api/v1/contracts/k1/amendments/a1': (r: Route) => json(r, detail(status)),
      'POST /api/v1/contracts/k1/amendments/a1/approve': (r: Route) => { status = 'Approved'; return json(r, detail('Approved')) },
    })
    await page.goto('/app/contratos/k1/aditivos/novo')
    await page.getByRole('textbox').first().fill('Mais uma parada')
    await page.getByLabel('Valor do acréscimo').fill('10000')
    await page.getByLabel('Descrição do marco 1').fill('Sinal do aditivo')
    await page.getByLabel('Percentual do marco 1').fill('100')
    await expect(page.getByText('Soma 100%')).toBeVisible()
    await page.getByRole('button', { name: 'Salvar aditivo' }).click()
    await expect(page.getByText('Aditivo salvo')).toBeVisible()
    expect(body).toMatchObject({ valueDelta: 10000, milestones: [{ description: 'Sinal do aditivo', percent: 100, trigger: 'Event', triggerEvent: 'Signature' }] })
    await page.getByRole('button', { name: 'Aprovar aditivo' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Cria 1 parcela a receber.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Aprovar aditivo' }).click()
    await expect(page.getByText('Aditivo aprovado', { exact: true })).toBeVisible()
  })
})
