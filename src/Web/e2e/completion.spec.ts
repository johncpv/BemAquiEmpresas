import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, status = 'Done') => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 2, awaitingApproval: false, selfApproved: false, selfInspection: false,
})
const order = (over: Record<string, unknown> = {}) => ({
  id: 'of1', number: 'OS-2026-00041', status: 'ReadyToComplete', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i1', productCode: 'ELEV-6', productName: 'Elevador 6 paradas',
  quantity: 1, priority: 0, routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte'), step(2, 'M', 'Montagem')], totalStandardHours: 4, currentStepIndex: -1,
  started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 7, qualityAttempt: 1, selfInspection: false, readyToComplete: true,
  openNonConformities: 0, completedAt: null, completedByName: null, completionBlocker: null, completionReversedAt: null, completionReversedByName: null, completionReversalReason: null, ...over,
})
const milestone = { description: 'Fabricação concluída', amount: 40000, dueDate: '2026-11-01', receivableId: 'r1', released: false, received: false }
const preview = (over: Record<string, unknown> = {}) => ({
  orderId: 'of1', orderNumber: 'OS-2026-00041', status: 'ReadyToComplete', canComplete: true, completeBlocker: null, canReverse: false, reverseBlocker: 'Só OS concluída tem a conclusão estornada.',
  lastFabricationOrder: true, otherOpenOrders: 0, projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectStatus: 'InProgress', projectFabricatedByOrders: false, showAmounts: true,
  milestones: [milestone], stockNotice: 'O produto acabado ainda não entra no estoque nesta versão.', version: 7, ...over,
})
const project = (over: Record<string, unknown> = {}) => ({
  id: 'p1', number: 'PRJ-2026-0007', name: 'Aurora — Elevador', status: 'InProgress', partyId: 'cli1', partyName: 'Condomínio Aurora', contractId: 'k1', contractNumber: 'CTR-2026-0004',
  siteAddressId: null, siteAddress: null, promisedDate: '2026-12-20', forecastDate: null, priority: null, managerUserId: 'u1', managerName: 'Ana Souza', createdAt: '2026-09-01T12:00:00Z',
  items: [{ id: 'pi1', sequence: 1, description: 'Elevador 6 paradas', quantity: 1, itemId: 'i1', quoteItemId: 'qi1' }], version: 3, fabricatedOn: null, deliveredOn: null,
  deliveredManually: false, hasFabricationMilestone: true, contractStatus: 'Active', isOnboarding: false, fabricatedByOrders: false, fabricationOrders: 1, openFabricationOrders: 1, ...over,
})

async function completionApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, finance: { actions: ALL } as never }) }),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, []),
    'GET /api/v1/contracts/k1': (r: Route) => json(r, { id: 'k1', number: 'CTR-2026-0004', quoteId: 'q1', quoteNumber: 'ORC-2026-0011', totalValue: 100000, milestones: [], status: 'Active' }),
    'GET /api/v1/attachments/Project/p1': (r: Route) => json(r, []),
    ...overrides,
  })
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

test.describe('Conclusão da OS (PRD-06a1)', () => {
  test('conclui a última OS do projeto: o projeto fica Fabricado e a parcela é liberada sozinha', async ({ page }, info) => {
    let current = order()
    let projectNow = project()
    let posted: unknown
    await completionApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, preview()),
      'POST /api/v1/production/production-orders/of1/complete': (r: Route) => {
        posted = r.request().postDataJSON()
        current = order({ status: 'Completed', completedAt: '2026-10-22T14:30:00Z', completedByName: 'Rui Supervisor', version: 8 })
        projectNow = project({ status: 'Fabricated', fabricatedOn: '2026-10-22', fabricatedByOrders: true, openFabricationOrders: 0 })
        return json(r, current)
      },
      'GET /api/v1/projects/p1': (r: Route) => json(r, projectNow),
      'GET /api/v1/projects/p1/timeline': (r: Route) => json(r, [
        { at: '2026-10-22T03:00:00Z', kind: 'fabricated', title: 'Fabricação concluída pelas OS', who: 'Sistema', link: '/app/producao/os?projeto=p1' },
        { at: '2026-10-22T14:30:00Z', kind: 'production_order_completed', title: 'OS-2026-00041 concluída', who: 'Rui Supervisor', link: '/app/producao/os/of1' },
      ]),
    })

    await page.goto('/app/producao/os/of1')
    await expect(page.getByRole('heading', { name: 'OS-2026-00041 — Aurora — Elevador' })).toBeVisible()
    await expect(page.getByText('Pronta para concluir').first()).toBeVisible()
    await page.getByRole('button', { name: 'Concluir OS' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('A OS-2026-00041 passa a Concluída.')).toBeVisible()
    await expect(dialog.getByText('O projeto PRJ-2026-0007 passa a Fabricado.')).toBeVisible()
    await expect(dialog.getByText(/Libera a parcela Fabricação concluída de R\$\s40\.000,00, vencendo em 01\/11\/2026\./)).toBeVisible()
    await expect(dialog.getByText('O produto acabado ainda não entra no estoque nesta versão.')).toBeVisible()
    await expect(dialog.getByRole('spinbutton')).toHaveCount(0)
    if (info.project.name === 'celular') {
      const box = await dialog.boundingBox()
      expect(box?.width).toBe(400)
      await expectNoHorizontalScroll(page)
    }
    await dialog.getByRole('button', { name: 'Concluir OS' }).click()
    await expect(page.getByText('OS concluída').first()).toBeVisible()
    expect(posted).toEqual({ version: 7 })
    await expect(page.getByText(/Concluída em 22\/10\/2026.* por Rui Supervisor/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Estornar conclusão' })).toBeVisible()

    // Próximo passo: a parcela liberada e o projeto, já Fabricado pelas OS.
    await expect(page.getByRole('button', { name: 'Ver parcela liberada' })).toBeVisible()
    await page.getByRole('button', { name: 'Ver projeto' }).click()
    await expect(page).toHaveURL(/\/app\/projetos\/p1/)
    await expect(page.getByText('Fabricação concluída em 22/10/2026 pelas OS.')).toBeVisible()
    await expect(page.getByText('Fabricação concluída pelas OS').first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Registrar fabricação concluída' })).toHaveCount(0)
  })

  test('"Concluir OS" desabilitado diz o motivo e o estorno recusado mostra o porquê no diálogo', async ({ page }) => {
    let current = order({ status: 'Quality', readyToComplete: false, completionBlocker: 'Falta aprovar a Qualidade.' })
    await completionApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, preview({ status: 'Completed', canComplete: false, completeBlocker: 'A OS já foi concluída.', canReverse: true, reverseBlocker: null,
        projectStatus: 'Fabricated', projectFabricatedByOrders: true, milestones: [{ ...milestone, released: true }] })),
      'POST /api/v1/production/production-orders/of1/reverse-completion': (r: Route) => json(r, {
        type: 'about:blank', title: 'Conflito', status: 409, code: 'fabrication_reversal_blocked', detail: 'A parcela Fabricação concluída já foi recebida. Fale com o Financeiro antes de estornar.' }, 409),
    })

    await page.goto('/app/producao/os/of1')
    await expect(page.getByRole('button', { name: 'Concluir OS' })).toBeDisabled()
    await expect(page.getByText('Falta aprovar a Qualidade.')).toBeVisible()

    current = order({ status: 'Completed', completedAt: '2026-10-22T14:30:00Z', completedByName: 'Rui Supervisor' })
    await page.reload()
    await page.getByRole('button', { name: 'Estornar conclusão' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('O projeto PRJ-2026-0007 volta de Fabricado a Em andamento.')).toBeVisible()
    await expect(dialog.getByText('A parcela Fabricação concluída já liberada continua em aberto. O Financeiro é avisado.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Estornar conclusão' })).toBeDisabled()
    await dialog.getByLabel('Motivo').fill('Medida errada na cabine')
    await dialog.getByRole('button', { name: 'Estornar conclusão' }).click()
    await expect(dialog.getByText('A parcela Fabricação concluída já foi recebida. Fale com o Financeiro antes de estornar.')).toBeVisible()
  })

  test('a lista abre em "Aguardando você" para quem conclui e o projeto com OS não tem o registro manual', async ({ page }) => {
    let situation: string | null = null
    await completionApi(page, {
      'GET /api/v1/production/production-orders': (r: Route) => { situation = new URL(r.request().url()).searchParams.get('situation'); return json(r, { items: [], page: 1, pageSize: 20, total: 0 }) },
      'GET /api/v1/production/work-centers': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
      'GET /api/v1/projects/p1': (r: Route) => json(r, project({ fabricationOrders: 2, openFabricationOrders: 2 })),
      'GET /api/v1/projects/p1/timeline': (r: Route) => json(r, []),
    })

    await page.goto('/app/producao/os')
    await expect(page).toHaveURL(/situacao=pronta/)
    await expect(page.getByText('Nenhuma OS pronta para concluir.')).toBeVisible()
    expect(situation).toBe('pronta')
    await expectNoHorizontalScroll(page)

    await page.goto('/app/projetos/p1?aba=linha-do-tempo')
    await expect(page.getByText('A fabricação deste projeto é concluída pelas OS (2 abertas).')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver as OS' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Registrar fabricação concluída' })).toHaveCount(0)
  })
})