import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const step = (i: number, code: string, name: string, status: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 0, awaitingApproval: false, selfApproved: false, selfInspection: false,
})
const order = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i9', productCode: 'ELEV-6', productName: 'Elevador 6 paradas',
  quantity: 1, priority: 0, routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte', 'Done'), step(2, 'M', 'Montagem', 'Ready')], totalStandardHours: 4,
  currentStepIndex: 1, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 7, qualityAttempt: 1, selfInspection: false,
  readyToComplete: false, openNonConformities: 0, completedAt: null, completedByName: null, completionBlocker: 'Falta concluir a etapa Montagem.', completionReversedAt: null,
  completionReversedByName: null, completionReversalReason: null,
}
const material = {
  productionOrderId: 'of1', bomReleased: true, bomRevision: 1, bomReleasedAt: '2026-10-23T12:00:00Z', reconciling: false, orderClosed: false, totalShortage: 0, totalOverIssued: 0,
  lines: [{
    itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', bomLineId: 'b1', required: 10, reserved: 10, issued: 0, shortage: 0, overIssued: 0, isBackfill: false,
    locations: [{ reservationId: 'r1', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', requested: 10, reserved: 10, issued: 0, shortage: 0, status: 'Active' }], purchaseRequestLink: null,
  }],
}
const lots = [
  { lotId: 'lt1', lotNumber: 'L-0925', expiry: '2026-11-30', onHand: 6, suggested: true },
  { lotId: 'lt2', lotNumber: 'L-1001', expiry: null, onHand: 20, suggested: false },
]
const draft = {
  productionOrderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', orderClosed: false, bomReleased: true, approvalLimit: 1000,
  lines: [{
    reservationId: 'r1', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', tracksLot: true, locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal',
    reserved: 10, onHand: 26, available: 16, averageCost: 100, lots, picks: [{ lotId: 'lt1', lotNumber: 'L-0925', quantity: 6, suggested: true }, { lotId: 'lt2', lotNumber: 'L-1001', quantity: 4, suggested: false }],
  }],
}
const lineView = (id: string, quantity: number, lot: string, over: Record<string, unknown> = {}) => ({
  id, sequence: 1, reservationId: 'r1', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal',
  lotId: lot, lotNumber: lot === 'lt1' ? 'L-0925' : 'L-1001', quantity, overQuantity: 0, overReason: null, isExtra: false, returnsLineId: null, movementId: null, movementNumber: null,
  unitCost: null, amount: null, returnable: 0, ...over,
})
const detail = (status: string, over: Record<string, unknown> = {}) => ({
  id: 'req1', number: 'REQ-2026-0003', kind: 'Issue', status, productionOrderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', stepId: 's2',
  stepName: 'Montagem', requestedByName: 'Diego Santos', requestedAt: '2026-10-23T13:00:00Z', completedByName: status === 'Completed' ? 'Bruno Almoxarife' : null,
  completedAt: status === 'Completed' ? '2026-10-23T13:10:00Z' : null, decidedByName: null, decidedAt: null, decisionComment: null, selfApproved: false, rejectReason: null, overBomValue: 0,
  value: status === 'Completed' ? 300 : 0, reversedByName: null, reversedAt: null, reversalReason: null, reversalSelfApproved: false,
  lines: [lineView('l1', 3, 'lt1', status === 'Completed' ? { movementId: 'm1', movementNumber: 'MOV-2026-000050', unitCost: 100, amount: 300, returnable: 3 } : {})],
  canConfirm: status === 'Requested', canDecide: false, decideBlockedReason: null, canReverse: false, reverseBlockedReason: null, version: 4, ...over,
})
const summary = (status: string) => ({
  id: 'req1', number: 'REQ-2026-0003', kind: 'Issue', status, productionOrderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', stepName: 'Montagem',
  lineCount: 1, firstItem: 'MP-001 — Cabo de aço 8 mm', value: status === 'Completed' ? 300 : 0, overBomValue: 0, requestedByName: 'Diego Santos', requestedAt: '2026-10-23T13:00:00Z',
  fromOperator: true, awaitingMe: status === 'Requested',
})
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

/** Backend em memória da EST-03 (as regras reais estão nos testes de integração .NET). */
async function requisitionApi(page: Page) {
  const calls: { url: string; body: unknown; key?: string }[] = []
  const record = (r: Route) => calls.push({ url: new URL(r.request().url()).pathname, body: r.request().postDataJSON(), key: r.request().headers()['idempotency-key'] })
  const state = { status: 'Requested', returned: false }
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, inventory: { actions: ALL } as never, projects: { actions: ALL } as never }) }),
    'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
    'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, { orderId: 'of1', canComplete: false, completeBlocker: 'Falta concluir a etapa Montagem.', canReverse: false, milestones: [], version: 7 }),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/labor-costs': (r: Route) => json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', showsEntryCost: false, entries: [], steps: [], totalHours: 0, laborCost: 0, missingCount: 0, includesBackfill: false, missingPositions: [] }),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/inventory/production-orders/of1/reservations/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, material),
    'GET /api/v1/inventory/production-orders/of1/requisitions': (r: Route) => json(r, {
      productionOrderId: 'of1', orderClosed: false, orderCancelled: false, requisitions: [summary(state.status)],
      returnable: state.status === 'Completed' ? [{ issueLineId: 'l1', requisitionId: 'req1', requisitionNumber: 'REQ-2026-0003', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M',
        lotId: 'lt1', lotNumber: 'L-0925', locationId: 'alm', locationCode: 'ALM', issued: 3, returnable: state.returned ? 1 : 3, unitCost: 100 }] : [],
    }),
    'GET /api/v1/inventory/production-orders/of1/requisitions/draft': (r: Route) => json(r, draft),
    'POST /api/v1/inventory/production-orders/of1/requisitions': (r: Route) => { record(r); return json(r, { ...detail('Completed'), id: 'req2', number: 'REQ-2026-0004', stepId: null, stepName: null }, 201) },
    'POST /api/v1/inventory/production-orders/of1/returns': (r: Route) => { record(r); state.returned = true; return json(r, { ...detail('Completed'), id: 'req5', number: 'REQ-2026-0005', kind: 'Return' }, 201) },
    'GET /api/v1/inventory/production-orders/of1/material-cost': (r: Route) => json(r, { productionOrderId: 'of1', items: [{ itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', quantity: state.returned ? 1 : 3, amount: state.returned ? 100 : 300 }], total: state.returned ? 100 : 300 }),
    'GET /api/v1/inventory/requisitions': (r: Route) => json(r, page1(state.status === 'Requested' || new URL(r.request().url()).searchParams.get('awaitingMe') !== 'true' ? [summary(state.status)] : [])),
    'GET /api/v1/inventory/requisitions/to-pick': (r: Route) => json(r, [{ productionOrderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', currentStepName: 'Montagem', items: 1, reserved: 10, openRequests: state.status === 'Requested' ? 1 : 0 }]),
    'GET /api/v1/inventory/requisitions/req1': (r: Route) => json(r, detail(state.status)),
    'POST /api/v1/inventory/requisitions/req1/confirm': (r: Route) => { record(r); state.status = 'Completed'; return json(r, detail('Completed')) },
    'GET /api/v1/inventory/locations': (r: Route) => json(r, page1([{ id: 'alm', code: 'ALM', name: 'Almoxarifado principal', isActive: true, isDefault: true, seedKey: 'alm', importSource: null, createdAt: '2026-10-01T00:00:00Z' }])),
    'GET /api/v1/inventory/items/search': (r: Route) => json(r, []),
  })
  return { calls, state }
}

const operator = { employeeId: 'e1', name: 'Diego Santos', firstName: 'Diego', workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', today: '2026-10-23' }
const queueStep = {
  stepId: 's2', orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'M', stepName: 'Montagem', stepIndex: 1, codes: ['C', 'M'], stepNames: ['Corte', 'Montagem'], status: 'Paused',
  workCenterId: 'c1', workCenterName: 'Montagem', isMyCenter: true, productName: 'Elevador 6 paradas', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', standardHours: 2, appliedHours: 1, people: ['Diego Santos'], pausedReason: 'Falta de material', awaitingApproval: false,
}
const stepDetail = () => {
  const startedAt = new Date(Date.now() - 3600 * 1000).toISOString()
  return {
    step: queueStep, productCode: 'ELEV-6', quantity: 1, checklist: [], people: [{ name: 'Diego Santos', paused: true, pauseReason: 'Material', startedAt, isMe: true }],
    mine: { entryId: 'le1', startedAt, closedPauseSeconds: 0, pauseStartedAt: new Date().toISOString(), pauseReason: 'Material', pauseNotes: null, netMinutes: 60 },
    attachments: [], requiresApproval: false, isQualityStep: false, nextStepName: null, suggestedPauseReason: 'Material', canStart: false, startBlockedReason: null, serverNow: new Date().toISOString(),
  }
}

test.describe('Requisição de material da OS (EST-03)', () => {
  test('operador pausado por falta de material pede material no /fab, sem custo nem lote', async ({ page }, info) => {
    const requests: { lines: { itemId: string; quantity: number }[] }[] = []
    let key: string | undefined
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules() }),
      'GET /api/v1/shopfloor/queue': (r: Route) => json(r, { operator, current: queueStep, steps: [], serverNow: new Date().toISOString() }),
      'GET /api/v1/shopfloor/steps/s2': (r: Route) => json(r, stepDetail()),
      'GET /api/v1/shopfloor/steps/s2/material': (r: Route) => json(r, {
        stepId: 's2', orderNumber: 'OS-2026-00041', bomReleased: true,
        items: [{ itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', required: 10, reserved: 10, issued: 0 }],
        requests: requests.map((q, i) => ({ id: `q${i}`, number: `REQ-2026-000${i + 3}`, state: 'Waiting', declineReason: null, requestedAt: '2026-10-23T13:00:00Z', decidedAt: null,
          lines: q.lines.map((l) => ({ itemId: l.itemId, itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', quantity: l.quantity })) })),
      }),
      'POST /api/v1/shopfloor/steps/s2/material-requests': (r: Route) => {
        requests.push(r.request().postDataJSON())
        key = r.request().headers()['idempotency-key']
        return json(r, { id: 'q0', number: 'REQ-2026-0003', state: 'Waiting', declineReason: null, requestedAt: '2026-10-23T13:00:00Z', decidedAt: null, lines: [] })
      },
    })
    await page.goto('/fab/etapa/s2')
    const section = page.locator('section[aria-labelledby="material-etapa"]')
    await expect(section.getByText('Pausou por falta de material? Peça ao almoxarifado o que falta.')).toBeVisible()
    await section.getByRole('button', { name: 'Pedir material', exact: true }).click()
    const sheet = page.getByRole('dialog', { name: 'Pedir material · OS-2026-00041' })
    await expect(sheet.getByText('BOM 10 · reservado 10 · baixado 0 M')).toBeVisible()
    await expect(sheet.getByText(/R\$|custo|lote/i)).toHaveCount(0)
    await sheet.getByRole('button', { name: 'Mais Cabo de aço 8 mm' }).click()
    await sheet.getByRole('button', { name: 'Mais Cabo de aço 8 mm' }).click()
    await sheet.getByRole('button', { name: 'Mais Cabo de aço 8 mm' }).click()
    await expect(sheet.getByRole('textbox', { name: 'Quantidade de Cabo de aço 8 mm (M)' })).toHaveValue('3')
    await sheet.getByRole('button', { name: 'Pedir material', exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Pedido enviado ao almoxarifado' }).first()).toBeVisible()
    expect(requests[0]).toEqual({ lines: [{ itemId: 'i1', quantity: 3 }] })
    expect(key).toMatch(/[0-9a-f-]{36}/)
    await expect(page.getByRole('list', { name: 'Pedidos de material desta etapa' })).toContainText('REQ-2026-0003')
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
  })

  test('almoxarife confirma o pedido escolhendo o lote e vê o próximo passo do custo do projeto', async ({ page }, info) => {
    const { calls } = await requisitionApi(page)
    await page.goto('/app/estoque/requisicoes')
    await expect(page.getByRole('heading', { name: 'Estoque: Requisições' })).toBeVisible()
    await expect(page.getByText('Mostrando: Aguardando você')).toBeVisible()
    await expect(page.getByRole('region', { name: /Para separar hoje/ }).or(page.locator('section[aria-labelledby="para-separar"]'))).toContainText('1 pedido do operador')
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
    await page.getByRole('link', { name: 'REQ-2026-0003' }).first().click()
    await expect(page.getByRole('heading', { name: 'REQ-2026-0003 — Requisição da OS-2026-00041' })).toBeVisible()
    await page.getByRole('button', { name: 'Confirmar pedido' }).click()

    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Sugerido', { exact: true })).toBeVisible()
    await dialog.getByRole('combobox', { name: 'Lote de MP-001' }).selectOption('lt2')
    await expect(dialog.getByText('Sugerido', { exact: true })).toHaveCount(0)
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText('Entra no custo do projeto PRJ-2026-0007.')
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText('disponível 16 → 16')
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    if (info.project.name === 'celular') {
      const box = await dialog.boundingBox()
      expect(box!.width).toBeGreaterThanOrEqual(399)
    }

    await dialog.getByRole('button', { name: 'Baixar material' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Material baixado' }).first()).toBeVisible()
    const confirm = calls.find((c) => c.url.endsWith('/confirm'))!
    expect(confirm.body).toEqual({ lines: [{ reservationId: 'r1', itemId: 'i1', locationId: 'alm', lotId: 'lt2', quantity: 3, overReason: null }], version: 4 })
    expect(confirm.key).toMatch(/[0-9a-f-]{36}/)
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toContainText('Ver custo do projeto')
  })

  test('requisita da aba Material com o lote sugerido, devolve parte e vê o Material na aba Custos', async ({ page }, info) => {
    const { calls, state } = await requisitionApi(page)
    state.status = 'Completed'
    await page.goto('/app/producao/os/of1?aba=material')
    await page.getByRole('button', { name: 'Requisitar material', exact: true }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('textbox', { name: 'Quantidade de MP-001' })).toHaveCount(2)
    await expect(dialog.getByText('Sugerido', { exact: true })).toBeVisible()
    await dialog.getByRole('button', { name: 'Baixar material' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Material baixado' }).first()).toBeVisible()
    const issue = calls.find((c) => c.url.endsWith('/production-orders/of1/requisitions'))!
    expect(issue.body).toEqual({ lines: [
      { reservationId: 'r1', itemId: 'i1', locationId: 'alm', lotId: 'lt1', quantity: 6, overReason: null },
      { reservationId: 'r1', itemId: 'i1', locationId: 'alm', lotId: 'lt2', quantity: 4, overReason: null },
    ] })

    await page.getByRole('button', { name: 'Devolver ao estoque', exact: true }).click()
    const back = page.getByRole('alertdialog')
    await back.getByRole('textbox', { name: 'Quantidade de MP-001' }).fill('2')
    await expect(back.getByRole('group', { name: 'Impacto' })).toContainText('R$ 200,00')
    await expect(back.getByRole('group', { name: 'Impacto' })).toContainText('Sai do custo do projeto PRJ-2026-0007.')
    await back.getByRole('button', { name: 'Devolver ao estoque', exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Material devolvido' }).first()).toBeVisible()
    expect(calls.find((c) => c.url.endsWith('/returns'))!.body).toEqual({ locationId: 'alm', lines: [{ issueLineId: 'l1', quantity: 2 }] })

    await page.goto('/app/producao/os/of1?aba=custos')
    await expect(page.getByRole('list', { name: 'Material por item' })).toContainText('MP-001')
    await expect(page.getByRole('list', { name: 'Material por item' })).toContainText('R$ 100,00')
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
  })
})
