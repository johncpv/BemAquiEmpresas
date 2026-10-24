import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status: 'Done', isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 2, awaitingApproval: false, selfApproved: false, selfInspection: false,
})
const order = (over: Record<string, unknown> = {}) => ({
  id: 'of1', number: 'OS-2026-00041', status: 'ReadyToComplete', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i1', productCode: 'ELV-6', productName: 'Elevador 6 paradas',
  quantity: 2, priority: 0, routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte'), step(2, 'M', 'Montagem')], totalStandardHours: 4, currentStepIndex: -1,
  started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 7, qualityAttempt: 1, selfInspection: false, readyToComplete: true,
  openNonConformities: 0, completedAt: null, completedByName: null, completionBlocker: null, completionReversedAt: null, completionReversedByName: null, completionReversalReason: null,
  entryMovementId: null, completedWithoutStock: false, ...over,
})
const stock = (over: Record<string, unknown> = {}) => ({
  enabled: true, productId: 'i1', productCode: 'ELV-6', productName: 'Elevador 6 paradas', unitCode: 'UN', quantity: 2, tracksSerial: true, tracksLot: false,
  suggestedSerials: ['ELV-6-2026-00001', 'ELV-6-2026-00002'], suggestedLocationId: 'l1',
  locations: [{ id: 'l1', code: 'ALM', name: 'Almoxarifado principal', isDefault: true }, { id: 'l2', code: 'EXP', name: 'Expedição', isDefault: false }],
  reservationsToRelease: [{ itemId: 'm1', itemCode: 'CH-12', itemName: 'Chapa 12 mm', unitCode: 'KG', locationName: 'Almoxarifado principal', quantity: 8 }],
  zeroCost: false, entryMovementId: null, entryMovementNumber: null, serialsToCancel: 0, shippedSerials: 0, completedWithoutStock: false, ...over,
})
const preview = (over: Record<string, unknown> = {}) => ({
  orderId: 'of1', orderNumber: 'OS-2026-00041', status: 'ReadyToComplete', canComplete: true, completeBlocker: null, canReverse: false, reverseBlocker: 'Só OS concluída tem a conclusão estornada.',
  lastFabricationOrder: false, otherOpenOrders: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectStatus: 'InProgress', projectFabricatedByOrders: false, showAmounts: true,
  milestones: [], stockNotice: null, version: 7, stock: stock(), ...over,
})
const serials = {
  productionOrderId: 'of1', productId: 'i1', productCode: 'ELV-6', productName: 'Elevador 6 paradas', tracksSerial: true, entryMovementId: 'mv1', entryMovementNumber: 'MOV-2026-000123',
  entryQuantity: 2, unitCode: 'UN', entryLocationName: 'Almoxarifado principal', entryPostedAt: '2026-10-23T14:30:00Z', entryReversed: false,
  serials: [
    { id: 'se1', serialNumber: 'ELV-6-2026-00001', status: 'InStock', locationId: 'l1', locationName: 'Almoxarifado principal', createdAt: '2026-10-23T14:30:00Z' },
    { id: 'se2', serialNumber: 'ELV-6-2026-CAB02', status: 'InStock', locationId: 'l1', locationName: 'Almoxarifado principal', createdAt: '2026-10-23T14:30:00Z' },
  ],
  consumedLots: [
    { itemId: 'm1', itemCode: 'CH-12', itemName: 'Chapa 12 mm', unitCode: 'KG', lotId: null, lotNumber: null, quantity: 4 },
    { itemId: 'm2', itemCode: 'CB-8', itemName: 'Cabo de aço 8 mm', unitCode: 'M', lotId: 'lt1', lotNumber: 'LT-0042', quantity: 2 },
  ],
}

async function finishedApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, inventory: { actions: ALL } as never, projects: { actions: [...ALL, 'view_cost'] } as never }) }),
    'GET /api/v1/inventory/units': (r: Route) => json(r, [{ id: 'u1', code: 'UN', name: 'Unidade', isActive: true, seedKey: 'un', itemCount: 1 }]),
    'GET /api/v1/inventory/categories': (r: Route) => json(r, []),
    'GET /api/v1/inventory/warranty-defaults': (r: Route) => json(r, []),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, []),
    'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, { productionOrderId: 'of1', bomReleased: true, bomRevision: 0, reconciling: false, lines: [], shortageCount: 0 }),
    'GET /api/v1/inventory/production-orders/of1/serials': (r: Route) => json(r, serials),
    'GET /api/v1/production/production-orders/of1/completion/cost': (r: Route) => json(r, { labor: 200, material: 500, total: 700, unit: 350 }),
    ...overrides,
  })
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

test.describe('Produto acabado na conclusão da OS (PRD-06a2)', () => {
  test('conclui a OS com requisição e horas: séries editáveis, local sugerido, custo unitário e as séries na OS', async ({ page }, info) => {
    let current = order()
    let posted: Record<string, unknown> | undefined
    await finishedApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, preview()),
      'POST /api/v1/production/production-orders/of1/complete': (r: Route) => {
        posted = r.request().postDataJSON() as Record<string, unknown>
        current = order({ status: 'Completed', completedAt: '2026-10-23T14:30:00Z', completedByName: 'Rui Supervisor', version: 8, entryMovementId: 'mv1' })
        return json(r, current)
      },
    })

    await page.goto('/app/producao/os/of1')
    await page.getByRole('button', { name: 'Concluir OS' }).first().click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Entra no estoque: 2 UN de ELV-6 · Elevador 6 paradas em Almoxarifado principal.')).toBeVisible()
    await expect(dialog.getByText('Gera 2 números de série, um por unidade.')).toBeVisible()
    await expect(dialog.getByText('Libera a reserva de 8 KG de CH-12 em Almoxarifado principal.')).toBeVisible()
    await expect(dialog.getByText('Sugerido')).toBeVisible()
    await expect(dialog.getByText('Custo apurado da OS')).toBeVisible()
    await expect(dialog.getByText(/custo unitário R\$\s350,000000/)).toBeVisible()
    await expect(dialog.getByText('O produto acabado ainda não entra no estoque nesta versão.')).toHaveCount(0)
    const second = dialog.getByRole('textbox', { name: 'Número de série da unidade 2' })
    await expect(second).toHaveValue('ELV-6-2026-00002')
    await second.fill('ELV-6-2026-00001')
    await expect(dialog.getByText('O número de série ELV-6-2026-00001 se repete.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Concluir OS' })).toBeDisabled()
    await second.fill('ELV-6-2026-CAB02')
    if (info.project.name === 'celular') {
      const box = await dialog.boundingBox()
      expect(box?.width).toBe(400)
      await expectNoHorizontalScroll(page)
    }
    await dialog.getByRole('button', { name: 'Concluir OS' }).click()
    await expect(page.getByText('OS concluída. Produto acabado no estoque.').first()).toBeVisible()
    expect(posted).toEqual({ version: 7, locationId: 'l1', serialNumbers: ['ELV-6-2026-00001', 'ELV-6-2026-CAB02'], confirmZeroCost: false })

    await page.getByRole('button', { name: 'Ver séries' }).click()
    await expect(page).toHaveURL(/aba=series/)
    await expect(page.getByRole('link', { name: 'MOV-2026-000123' })).toBeVisible()
    await expect(page.getByText('ELV-6-2026-CAB02')).toBeVisible()
    await expect(page.getByText('No estoque').first()).toBeVisible()
    await expect(page.getByText(/CB-8 — Cabo de aço 8 mm: 2 M · lote LT-0042/)).toBeVisible()
    await expect(page.getByText('Cada série herda os lotes da OS inteira.')).toBeVisible()
    await expectNoHorizontalScroll(page)
  })

  test('custo zerado: o aviso âmbar pede a confirmação explícita', async ({ page }) => {
    let posted: Record<string, unknown> | undefined
    await finishedApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order()),
      'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, preview({ stock: stock({ zeroCost: true, tracksSerial: false, suggestedSerials: [], reservationsToRelease: [] }) })),
      'GET /api/v1/production/production-orders/of1/completion/cost': (r: Route) => json(r, { labor: 0, material: 0, total: 0, unit: 0 }),
      'POST /api/v1/production/production-orders/of1/complete': (r: Route) => { posted = r.request().postDataJSON() as Record<string, unknown>; return json(r, order({ status: 'Completed', version: 8 })) },
    })

    await page.goto('/app/producao/os/of1')
    await page.getByRole('button', { name: 'Concluir OS' }).first().click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Custo da OS zerado: o produto entra no estoque com custo 0.')).toBeVisible()
    await expect(dialog.getByText('Sem controle de série: a entrada é por quantidade.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Concluir OS' })).toBeDisabled()
    await dialog.getByLabel('Confirmo a entrada com custo 0').check()
    await dialog.getByRole('button', { name: 'Concluir OS' }).click()
    await expect(page.getByText('OS concluída. Produto acabado no estoque.').first()).toBeVisible()
    expect(posted).toMatchObject({ confirmZeroCost: true, serialNumbers: null })
  })

  test('OS sem item de estoque: o botão diz o motivo e o atalho escolhe o produto', async ({ page }) => {
    let current = order({ productId: null, productCode: null, productName: null, completionBlocker: 'Escolha o item de estoque do produto antes de concluir.' })
    let put: unknown
    await finishedApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'GET /api/v1/production/production-orders/of1/product-options': (r: Route) => json(r, [{ id: 'i9', code: 'ELV-9', name: 'Elevador 9 paradas', type: 'Finished', tracksSerial: true }]),
      'PUT /api/v1/production/production-orders/of1/product': (r: Route) => {
        put = r.request().postDataJSON()
        current = order({ productId: 'i9', productCode: 'ELV-9', productName: 'Elevador 9 paradas', version: 8 })
        return json(r, current)
      },
    })

    await page.goto('/app/producao/os/of1')
    await expect(page.getByRole('button', { name: 'Concluir OS' }).first()).toBeDisabled()
    await expect(page.getByText('Escolha o item de estoque do produto antes de concluir.')).toBeVisible()
    await page.getByRole('button', { name: 'Escolher item do produto' }).click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel(/ELV-9 — Elevador 9 paradas/).check()
    await dialog.getByRole('button', { name: 'Escolher item' }).click()
    await expect(page.getByText('Item do produto escolhido').first()).toBeVisible()
    expect(put).toEqual({ itemId: 'i9', version: 7 })
    await expect(page.getByRole('button', { name: 'Concluir OS' }).first()).toBeEnabled()
  })

  test('as séries do item: série, OS, projeto, situação e local; sem série, o vazio diz por quê', async ({ page }) => {
    const item = (tracksSerial: boolean) => ({
      id: 'i1', type: 'Finished', code: 'ELV-6', name: 'Elevador 6 paradas', unitId: 'u1', unitCode: 'UN', categoryId: null, categoryName: null, ncm: '84281000', cest: null, origin: 0, minStock: 0,
      tracksLot: false, tracksSerial, warrantyMonths: 12, averageCost: 350, isActive: true, importSource: null, createdAt: '2026-10-19T12:00:00Z',
    })
    let tracks = true
    await finishedApi(page, {
      'GET /api/v1/inventory/items/i1': (r: Route) => json(r, item(tracks)),
      'GET /api/v1/inventory/items/i1/serials': (r: Route) => json(r, { items: [
        { id: 'se1', serialNumber: 'ELV-6-2026-00001', status: 'InStock', locationId: 'l1', locationName: 'Almoxarifado principal', productionOrderId: 'of1', productionOrderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', createdAt: '2026-10-23T14:30:00Z' },
        { id: 'se0', serialNumber: 'ELV-6-2026-00000', status: 'Cancelled', locationId: 'l1', locationName: 'Almoxarifado principal', productionOrderId: 'of1', productionOrderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', createdAt: '2026-10-22T14:30:00Z' },
      ], page: 1, pageSize: 20, total: 2 }),
    })

    await page.goto('/app/estoque/itens/i1?aba=series')
    await expect(page.getByText('ELV-6-2026-00001')).toBeVisible()
    await expect(page.getByText('No estoque').first()).toBeVisible()
    await expect(page.getByText('Cancelada').first()).toBeVisible()
    await expectNoHorizontalScroll(page)

    tracks = false
    await page.reload()
    await expect(page.getByText('Este produto não controla série.')).toBeVisible()
  })
})
