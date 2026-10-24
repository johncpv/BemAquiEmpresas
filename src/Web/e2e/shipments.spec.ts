import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, status = 'Done') => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 2, awaitingApproval: false, selfApproved: false, selfInspection: false,
})
const order = (over: Record<string, unknown> = {}) => ({
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i1', productCode: 'ELV-6', productName: 'Elevador 6 paradas',
  quantity: 2, priority: 0, routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte'), step(2, 'M', 'Montagem'), step(3, 'E', 'Expedição', 'Ready')],
  totalStandardHours: 6, currentStepIndex: 2, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 6, qualityAttempt: 1,
  selfInspection: false, readyToComplete: false, openNonConformities: 0, completedAt: null, completedByName: null, completionBlocker: 'Falta concluir a etapa Expedição.',
  completionReversedAt: null, completionReversedByName: null, completionReversalReason: null, entryMovementId: null, completedWithoutStock: false, ...over,
})
const ready = () => order({ status: 'ReadyToComplete', readyToComplete: true, currentStepIndex: -1, completionBlocker: null, version: 7, steps: [step(1, 'C', 'Corte'), step(2, 'M', 'Montagem'), step(3, 'E', 'Expedição')] })
const completion = {
  orderId: 'of1', orderNumber: 'OS-2026-00041', status: 'ReadyToComplete', canComplete: true, completeBlocker: null, canReverse: false, reverseBlocker: 'Só OS concluída tem a conclusão estornada.',
  lastFabricationOrder: true, otherOpenOrders: 0, projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectStatus: 'InProgress', projectFabricatedByOrders: true, showAmounts: false,
  milestones: [], stockNotice: null, version: 7,
  stock: {
    enabled: true, productId: 'i1', productCode: 'ELV-6', productName: 'Elevador 6 paradas', unitCode: 'UN', quantity: 2, tracksSerial: true, tracksLot: false,
    suggestedSerials: ['ELV-6-2026-00001', 'ELV-6-2026-00002'], suggestedLocationId: 'l1', locations: [{ id: 'l1', code: 'ALM', name: 'Almoxarifado principal', isDefault: true }],
    reservationsToRelease: [], zeroCost: false, entryMovementId: null, entryMovementNumber: null, serialsToCancel: 0, shippedSerials: 0, completedWithoutStock: false,
  },
}
const serial = (n: number, open: string | null = null) => ({ id: `se${n}`, serialNumber: `ELV-6-2026-0000${n}`, productionOrderId: 'of1', openShipmentNumber: open })
const available = (over: Record<string, unknown> = {}) => ({
  itemId: 'i1', itemCode: 'ELV-6', itemName: 'Elevador 6 paradas', unitCode: 'UN', locationId: 'l1', locationName: 'Almoxarifado principal', lotId: null, lotNumber: null, tracksSerial: true,
  quantity: 2, onHand: 2, reserved: 0, available: 2, shippable: 2, inOpenShipments: 0, fromGeneralStock: false, serials: [serial(1), serial(2)], ...over,
})
const next = (over: Record<string, unknown> = {}) => ({
  canConfirm: true, confirmBlocker: null, canConfirmStock: false, confirmStockBlocker: null, installationOrderId: null, installationOrderNumber: null, projectDelivered: false,
  projectStatus: 'Fabricated', ...over,
})
const shipment = (over: Record<string, unknown> = {}) => ({
  id: 'r1', number: 'ROM-2026-0001', status: 'Draft', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
  deliveryAddress: 'Rua da Obra, 100 — Centro — Belo Horizonte/MG — CEP 30100-000', carrierPartyId: 'tr1', carrierName: 'Transportes Rápidos', vehiclePlate: 'ABC1D23',
  driverName: 'José da Silva', volumes: 2, grossWeight: 850.5, plannedDate: '2026-10-28', hasGeneralStock: false, createdAt: '2026-10-24T12:00:00Z', createdBy: 'u1',
  createdByName: 'Ana Souza', stockApprovalRequestedAt: null, stockApprovedByName: null, selfApproved: false, shippedAt: null, shippedByName: null, cancelledAt: null,
  cancelledByName: null, cancelReason: null, reversedAt: null, reversedByName: null, reversalReason: null, pdfAttachmentId: null,
  lines: [{
    id: 'ln1', sequence: 1, itemId: 'i1', itemCode: 'ELV-6', itemName: 'Elevador 6 paradas', unitCode: 'UN', quantity: 2, locationId: 'l1', locationName: 'Almoxarifado principal', lotId: null,
    lotNumber: null, fromGeneralStock: false, serials: [serial(1), serial(2)], movementId: null,
  }],
  next: next(), version: 3, ...over,
})

async function shipmentApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, inventory: { actions: ALL } as never, projects: { actions: ALL } as never }) }),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', entries: [], totalHours: 0, awaitingApprovalStepId: null, awaitingApprovalStepName: null, version: 4 }),
    'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, { productionOrderId: 'of1', bomReleased: true, bomRevision: 0, reconciling: false, lines: [], shortageCount: 0 }),
    'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, completion),
    'GET /api/v1/production/production-orders/of1/completion/cost': (r: Route) => json(r, { labor: 200, material: 500, total: 700, unit: 350 }),
    'GET /api/v1/production/shipments/form': (r: Route) => json(r, {
      projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
      deliveryAddress: 'Rua da Obra, 100 — Centro — Belo Horizonte/MG — CEP 30100-000', suggestedCarrierId: 'tr1',
      carriers: [{ id: 'tr1', name: 'Transportes Rápidos', suggested: true }, { id: 'tr2', name: 'Frete Sul', suggested: false }], available: [available()], productionOrderId: 'of1',
    }),
    'GET /api/v1/production/shipments/general-stock': (r: Route) => json(r, []),
    'GET /api/v1/attachments/Shipment/r1': (r: Route) => json(r, []),
    'GET /api/v1/production/shipments/r1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    'GET /api/v1/production/shipments/r1/impact': (r: Route) => json(r, {
      id: 'r1', number: 'ROM-2026-0001', status: 'Draft', requiresStockApproval: false, version: 3,
      lines: [{ lineId: 'ln1', itemCode: 'ELV-6', itemName: 'Elevador 6 paradas', unitCode: 'UN', locationName: 'Almoxarifado principal', quantity: 2, onHandBefore: 2, onHandAfter: 0, serialNumbers: ['ELV-6-2026-00001', 'ELV-6-2026-00002'], fromGeneralStock: false, insufficient: false }],
    }),
    'GET /api/v1/production/shipments/r1/pdf': (r: Route) => r.fulfill({ status: 200, contentType: 'application/pdf', headers: { 'content-disposition': 'attachment; filename=ROM-2026-0001.pdf' }, body: '%PDF-1.7 romaneio' }),
    ...overrides,
  })
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

test.describe('Expedição: romaneio e saída do produto acabado (PRD-06b)', () => {
  test('finaliza a etapa Expedição, conclui a OS, monta o romaneio pelo próximo passo, baixa o PDF e confirma a expedição', async ({ page }, info) => {
    let current = order()
    let detail = shipment()
    const sent: Record<string, unknown> = {}
    await shipmentApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'POST /api/v1/production/production-orders/of1/steps/s3/complete': (r: Route) => { current = ready(); return json(r, current) },
      'POST /api/v1/production/production-orders/of1/complete': (r: Route) => {
        current = order({ status: 'Completed', readyToComplete: false, currentStepIndex: -1, completionBlocker: null, completedAt: '2026-10-24T11:00:00Z', completedByName: 'Ana Souza', entryMovementId: 'mv1', version: 8 })
        return json(r, current)
      },
      'POST /api/v1/production/shipments': (r: Route) => { sent.create = r.request().postDataJSON(); sent.key = r.request().headers()['idempotency-key']; return json(r, detail, 201) },
      'GET /api/v1/production/shipments/r1': (r: Route) => json(r, detail),
      'POST /api/v1/production/shipments/r1/confirm': (r: Route) => {
        sent.confirm = r.request().postDataJSON()
        detail = shipment({ status: 'Shipped', shippedAt: '2026-10-24T12:30:00Z', shippedByName: 'Ana Souza', pdfAttachmentId: 'a1', next: next({ canConfirm: false, confirmBlocker: 'Só romaneio em rascunho é confirmado.' }), version: 4 })
        return json(r, detail)
      },
    })

    // A etapa "Expedição" do roteiro é preparar e embalar (D4): finalizada, a OS fica pronta e o próximo passo é concluir (CA 9).
    await page.goto('/app/producao/os/of1?aba=apontamentos&concluir=s3')
    const stepDialog = page.getByRole('alertdialog')
    await stepDialog.getByRole('textbox', { name: 'Motivo' }).fill('Embalado fora do sistema.')
    await stepDialog.getByRole('button', { name: 'Concluir etapa', exact: true }).click()
    const nextSteps = page.getByRole('region', { name: 'Próximos passos' })
    await nextSteps.getByRole('button', { name: 'Concluir OS' }).click()
    const complete = page.getByRole('alertdialog')
    await expect(complete.getByText('Entra no estoque: 2 UN de ELV-6 · Elevador 6 paradas em Almoxarifado principal.')).toBeVisible()
    await complete.getByRole('button', { name: 'Concluir OS' }).click()
    await expect(page.getByText('OS concluída. Produto acabado no estoque.').first()).toBeVisible()

    // PRD-06a2 → PRD-06b: "Novo romaneio" já com o projeto e as séries da OS.
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Novo romaneio' }).click()
    await expect(page).toHaveURL(/\/app\/producao\/expedicao\/novo\?projeto=p1&os=of1/)
    await expect(page.getByRole('checkbox', { name: 'ELV-6-2026-00001' })).toBeChecked()
    await expect(page.getByRole('checkbox', { name: 'ELV-6-2026-00002' })).toBeChecked()
    await expect(page.getByRole('textbox', { name: /Endereço de entrega/ })).toHaveValue(/Rua da Obra, 100/)
    await expect(page.getByText('Sugerido')).toBeVisible()
    await page.getByRole('textbox', { name: /Placa/ }).fill('abc1d23')
    await page.getByRole('textbox', { name: /Motorista/ }).fill('José da Silva')
    await page.getByRole('textbox', { name: /Volumes/ }).fill('2')
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
    await page.getByRole('button', { name: 'Salvar romaneio' }).click()
    await expect(page.getByText('Romaneio salvo').first()).toBeVisible()
    await expect(page).toHaveURL(/\/app\/producao\/expedicao\/r1/)
    expect(sent.key).toBeTruthy()
    expect(sent.create).toMatchObject({
      projectId: 'p1', carrierPartyId: 'tr1', vehiclePlate: 'abc1d23', driverName: 'José da Silva', volumes: 2,
      lines: [{ itemId: 'i1', locationId: 'l1', lotId: null, quantity: 2, serialIds: ['se1', 'se2'], fromGeneralStock: false }],
    })

    // CA 3: o PDF do romaneio.
    const download = page.waitForEvent('download')
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Baixar PDF' }).click()
    expect((await download).suggestedFilename()).toBe('ROM-2026-0001.pdf')

    // CA 4: o diálogo de impacto, a confirmação e os próximos passos (CA 8).
    await page.getByRole('button', { name: 'Confirmar expedição' }).first().click()
    const confirm = page.getByRole('alertdialog')
    await expect(confirm.getByText(/Saída de 2 UN de ELV-6 em Almoxarifado principal: 2 → 0 · séries ELV-6-2026-00001, ELV-6-2026-00002 passam a Expedido/)).toBeVisible()
    if (info.project.name === 'celular') {
      const box = await confirm.boundingBox()
      expect(box?.width).toBe(400)
      await expectNoHorizontalScroll(page)
    }
    await confirm.getByRole('button', { name: 'Confirmar expedição' }).click()
    await expect(page.getByText('Expedição confirmada').first()).toBeVisible()
    expect(sent.confirm).toEqual({ version: 3 })
    await expect(page.getByText('Expedido', { exact: true }).first()).toBeVisible()
    await expect(page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Registrar entrega' })).toBeVisible()
    await expect(page.getByText('Emitir NF-e: disponível na Fase 5.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Estornar expedição' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cancelar romaneio' })).toHaveCount(0)
    await expectNoHorizontalScroll(page)
  })

  test('Expedição lista os prontos para expedir e abre em "Aguardando você" para quem confirma o estoque; o saldo insuficiente aparece no diálogo', async ({ page }) => {
    let filter: string | null = null
    await shipmentApi(page, {
      'GET /api/v1/production/shipments/ready': (r: Route) => json(r, [{ projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyName: 'Condomínio Aurora', shippable: 2, lines: [available()] }]),
      'GET /api/v1/production/shipments': (r: Route) => {
        filter = new URL(r.request().url()).searchParams.get('filter')
        return json(r, { items: [{ id: 'r1', number: 'ROM-2026-0001', status: 'AwaitingStockApproval', projectId: 'p1', projectNumber: 'PRJ-2026-0007', partyName: 'Condomínio Aurora', lines: 2, units: 3, plannedDate: '2026-10-28', shippedAt: null, createdAt: '2026-10-24T12:00:00Z', hasGeneralStock: true }], total: 1, page: 1, pageSize: 25, awaitingMine: 1 })
      },
      'GET /api/v1/production/shipments/r1': (r: Route) => json(r, shipment()),
      'POST /api/v1/production/shipments/r1/confirm': (r: Route) => json(r, { status: 409, code: 'insufficient_stock', detail: 'Saldo insuficiente: há 1,0000 UN de Elevador 6 paradas em Almoxarifado principal.' }, 409),
    })

    await page.setViewportSize({ width: 400, height: 820 })
    await page.goto('/app/producao/expedicao')
    await expect(page.getByRole('heading', { name: 'Prontos para expedir' })).toBeVisible()
    await expect(page.getByText('Disponível 2').first()).toBeVisible()
    await expect(page.getByRole('button', { name: /Aguardando você \(1\)/ })).toHaveAttribute('aria-pressed', 'true')
    expect(filter).toBe('aguardando')
    await expect(page.getByText('Aguardando confirmação do estoque')).toBeVisible()
    await expect(page.getByText('Estoque geral').first()).toBeVisible()
    await expectNoHorizontalScroll(page)

    // CA 5: o 409 de saldo fica dentro do diálogo e o romaneio continua em rascunho.
    await page.goto('/app/producao/expedicao/r1')
    await page.getByRole('button', { name: 'Confirmar expedição' }).first().click()
    const confirm = page.getByRole('alertdialog')
    await confirm.getByRole('button', { name: 'Confirmar expedição' }).click()
    await expect(confirm.getByText('Saldo insuficiente: há 1,0000 UN de Elevador 6 paradas em Almoxarifado principal.')).toBeVisible()
    await expect(page.getByText('Rascunho').first()).toBeVisible()
  })

  test('sem nada pronto, o vazio leva às OS prontas para concluir', async ({ page }) => {
    await shipmentApi(page, {
      'GET /api/v1/production/shipments/ready': (r: Route) => json(r, []),
      'GET /api/v1/production/shipments': (r: Route) => json(r, { items: [], total: 0, page: 1, pageSize: 25, awaitingMine: 0 }),
    })
    await page.goto('/app/producao/expedicao')
    await expect(page.getByText('Nada pronto para expedir. As OS concluídas aparecem aqui.')).toBeVisible()
    await page.getByRole('link', { name: 'Ver OS prontas para concluir' }).click()
    await expect(page).toHaveURL(/\/app\/producao\/os\?situacao=pronta/)
  })
})
