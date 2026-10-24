import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'receive', 'pay']
const iso = (days: number) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

const lineA = { id: 'pl1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', quantity: 10, enteredQuantity: null, enteredUnitCode: null, unitPrice: 30,
  lineTotal: 300, costType: 'Material', quantityReceived: 0, requestLineIds: [], requests: [], quantityOverReceived: 0, amountReceived: 0, quantityOpen: 10 }
const lineB = { ...lineA, id: 'pl2', sequence: 2, itemId: 'i2', itemCode: 'MP-020', itemName: 'Cabo de aço 8 mm', quantity: 20, unitPrice: 35, lineTotal: 700, quantityOpen: 20 }

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: 'po1', number: 'PC-2026-0001', status: 'Approved', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, projectName: null, paymentTermId: 't2',
    paymentTermName: '30/60 dias', expectedDate: iso(5), daysLate: null, subtotal: 1000, freight: 100, ipiPct: 5, ipiAmount: 50, total: 1150, notes: null, rfqId: null, quotesCount: null,
    singleQuoteWarning: null, requestedBy: 'u2', requestedByName: 'Bruno Compras', createdAt: '2026-10-20T12:00:00Z', submittedAt: '2026-10-20T13:00:00Z', approvedAt: '2026-10-20T14:00:00Z',
    attempt: 1, referenceAmount: 1150, splitWarning: null, selfApproved: false, changedAfterSent: false, rejectionComment: null, cancelReason: null, cancelledAt: null, cancelledByName: null,
    quantityReceived: 0, lines: [lineA, lineB], levels: [], installments: [], isRequester: false, canEdit: false, canSubmit: false, canDecide: false, canCancel: false, decideBlocker: null,
    version: 9, approvedTotal: 1150, closedBalanceAmount: null, closeReason: null, closedAt: null, amountReceived: 0, receiptsCount: 0, canReceive: true, receiveBlocker: null, canClose: false,
    canCorrectPrice: false, ...overrides,
  }
}

const defaults = {
  orderId: 'po1', orderNumber: 'PC-2026-0001', status: 'Approved', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, today: iso(0), defaultLocationId: 'alm',
  locations: [{ id: 'alm', code: 'ALM', name: 'Almoxarifado', isDefault: true }, { id: 'exp', code: 'EXP', name: 'Expedição', isDefault: false }], tolerancePct: 0, orderSubtotal: 1000,
  orderFreight: 100, orderIpiAmount: 50, freightUsed: 0, ipiUsed: 0, approvedTotal: 1150, receivedAmount: 0, soleOwner: false, blocker: null, version: 9,
  lines: [
    { poLineId: 'pl1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', tracksLot: true, ordered: 10, received: 0, open: 10, maxQuantity: 10, unitPrice: 30, supplierUnit: null, supplierFactor: null },
    { poLineId: 'pl2', sequence: 2, itemId: 'i2', itemCode: 'MP-020', itemName: 'Cabo de aço 8 mm', unitCode: 'UN', tracksLot: false, ordered: 20, received: 0, open: 20, maxQuantity: 20, unitPrice: 35, supplierUnit: null, supplierFactor: null },
  ],
}

const preview = {
  receivedOn: iso(0), subtotal: 650, freight: 65, ipiAmount: 32.5, amount: 747.5, freightMax: 65, ipiMax: 32.5, ceiling: 1150, receivedAmountAfter: 747.5, orderCompletes: false, soleOwner: false,
  plannedBefore: 1150, plannedAfter: 402.5,
  lines: [
    { poLineId: 'pl1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', quantity: 10, lineAmount: 300, freightShare: 30, ipiShare: 15, unitCost: 34.5, averageCostBefore: 30, averageCostAfter: 32.25 },
    { poLineId: 'pl2', itemCode: 'MP-020', itemName: 'Cabo de aço 8 mm', unitCode: 'UN', quantity: 10, lineAmount: 350, freightShare: 35, ipiShare: 17.5, unitCost: 40.25, averageCostBefore: 0, averageCostAfter: 40.25 },
  ],
  installments: [{ number: 1, dueDate: iso(30), amount: 373.75 }, { number: 2, dueDate: iso(60), amount: 373.75 }],
}

const receipt = {
  id: 'rec1', number: 'REC-2026-0001', status: 'Completed', purchaseOrderId: 'po1', purchaseOrderNumber: 'PC-2026-0001', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null,
  receivedOn: iso(0), subtotal: 650, freight: 65, ipiAmount: 32.5, amount: 747.5, notes: null, receivedBy: 'u1', receivedByName: 'Ana Souza', soleOwner: false, createdAt: '2026-10-24T12:00:00Z',
  reversedAt: null, reversedByName: null, reversalReason: null, servedOrders: [{ id: 'os1', number: 'OS-2026-00012' }], canReverse: true, version: 1,
  lines: [
    { id: 'rl1', sequence: 1, poLineId: 'pl1', itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', quantity: 10, supplierUnit: null, supplierQuantity: null, lotNumber: 'L-2026-77',
      lotExpiry: null, locationId: 'alm', locationName: 'Almoxarifado', unitPrice: 30, lineAmount: 300, freightShare: 30, ipiShare: 15, unitCost: 34.5, movementId: 'm1', movementNumber: 'MOV-2026-000001', reversalMovementId: null },
    { id: 'rl2', sequence: 2, poLineId: 'pl2', itemId: 'i2', itemCode: 'MP-020', itemName: 'Cabo de aço 8 mm', unitCode: 'UN', quantity: 10, supplierUnit: null, supplierQuantity: null, lotNumber: null,
      lotExpiry: null, locationId: 'alm', locationName: 'Almoxarifado', unitPrice: 35, lineAmount: 350, freightShare: 35, ipiShare: 17.5, unitCost: 40.25, movementId: 'm2', movementNumber: 'MOV-2026-000001', reversalMovementId: null },
  ],
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

function receivingApi(state: { order: Record<string, unknown> }, posted: Record<string, unknown>) {
  return {
    'GET /api/v1/me/permissions': (r: Route) => json(r, {
      permissions: [], isOwner: false, version: '1',
      modules: allModules({ purchasing: { actions: ALL } as never, finance: { actions: ['view'] } as never, inventory: { actions: ['view'] } as never }),
    }),
    'GET /api/v1/purchasing/purchase-orders/po1': (r: Route) => json(r, state.order),
    'GET /api/v1/purchasing/purchase-orders/po1/receipt-defaults': (r: Route) => json(r, defaults),
    'POST /api/v1/purchasing/purchase-orders/po1/receipts/preview': async (r: Route) => {
      posted.preview = r.request().postDataJSON()
      await json(r, preview)
    },
    'POST /api/v1/purchasing/purchase-orders/po1/receipts': async (r: Route) => {
      posted.register = r.request().postDataJSON()
      posted.key = r.request().headers()['idempotency-key']
      state.order = order({ status: 'PartiallyReceived', amountReceived: 747.5, receiptsCount: 1, quantityReceived: 20, version: 10 })
      await json(r, receipt, 201)
    },
    'GET /api/v1/purchasing/goods-receipts/rec1': (r: Route) => json(r, receipt),
    'GET /api/v1/purchasing/goods-receipts/rec1/payables': (r: Route) => json(r, [
      { id: 'cp1', number: 'CP-2026-00041', installment: 1, status: 'Open', amount: 373.75, dueDate: iso(30), paymentOrderId: null },
      { id: 'cp2', number: 'CP-2026-00042', installment: 2, status: 'Open', amount: 373.75, dueDate: iso(60), paymentOrderId: null },
    ]),
    'GET /api/v1/purchasing/goods-receipts/rec1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/purchasing/purchase-orders/po1/payables': (r: Route) => json(r, [
      { id: 'pp1', number: 'CP-2026-00031', installment: 1, status: 'Planned', amount: 201.25, dueDate: iso(35), isEstimate: true, paymentOrderId: null },
      { id: 'pp2', number: 'CP-2026-00032', installment: 2, status: 'Planned', amount: 201.25, dueDate: iso(65), isEstimate: true, paymentOrderId: null },
    ]),
    'GET /api/v1/purchasing/purchase-orders/po1/decisions': (r: Route) => json(r, { levels: [], events: [] }),
    'GET /api/v1/purchasing/purchase-orders/po1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/purchasing/purchase-orders/po1/receipts': (r: Route) => json(r, []),
    'GET /api/v1/attachments/PurchaseOrder/po1': (r: Route) => json(r, []),
  }
}

test.describe('Recebimento manual (CMP-04)', () => {
  test('recebe em parte um pedido de dois itens com lote, vê o custo médio, o título do recebimento e o previsto reduzido', async ({ page }) => {
    const state = { order: order() }
    const posted: Record<string, unknown> = {}
    await mockApi(page, receivingApi(state, posted))
    await page.goto('/app/compras/pedidos/po1')
    await page.getByRole('button', { name: 'Registrar recebimento' }).click()
    await expect(page).toHaveURL(/\/app\/compras\/pedidos\/po1\/recebimento/)
    await expect(page.getByRole('heading', { name: 'Registrar recebimento — PC-2026-0001' })).toBeVisible({ timeout: 20_000 })

    const cards = page.getByRole('list', { name: 'Itens recebidos' }).getByRole('listitem')
    await expect(cards).toHaveCount(2)
    await expect(cards.nth(0)).toContainText('Saldo 10 UN')
    await expect(cards.nth(0).getByText('Sugerido')).toBeVisible()
    await cards.nth(0).getByRole('textbox', { name: /^Lote/ }).fill('L-2026-77')
    await cards.nth(1).getByRole('textbox', { name: /^Recebido/ }).fill('10')
    await expect(page.getByRole('textbox', { name: /^Frete deste recebimento/ })).toHaveValue('65')
    await expect(page.getByRole('textbox', { name: /^IPI deste recebimento/ })).toHaveValue('32,5')

    await page.getByRole('button', { name: 'Registrar recebimento' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Registrar o recebimento do PC-2026-0001?' })
    await expect(dialog).toBeVisible()
    await expect(dialog).toContainText('R$ 34,50')
    await expect(dialog).toContainText('Custo médio R$ 30,00 → R$ 32,25')
    await expect(dialog.getByRole('table', { name: 'Títulos a pagar do recebimento' }).getByRole('row')).toHaveCount(3)
    await expect(dialog).toContainText('R$ 1.150,00 → R$ 402,50')
    expect(posted.preview).toMatchObject({ freight: 65, ipiAmount: 32.5, version: 9, lines: [{ poLineId: 'pl1', quantity: 10, lotNumber: 'L-2026-77', locationId: 'alm' }, { poLineId: 'pl2', quantity: 10, lotNumber: null }] })
    await dialog.getByRole('button', { name: 'Registrar recebimento' }).click()

    await expect(page).toHaveURL(/\/app\/compras\/recebimentos\/rec1/)
    expect(posted.register).toMatchObject({ freight: 65, ipiAmount: 32.5, version: 9 })
    expect(posted.key).toBeTruthy()
    await expect(page.getByRole('heading', { name: 'REC-2026-0001 — Gama Aços' })).toBeVisible()
    const steps = page.getByRole('region', { name: 'Próximos passos' })
    await expect(steps.getByRole('button', { name: 'Ver OS que receberam material' })).toBeVisible()
    await expect(steps.getByRole('button', { name: 'Ver movimentos de estoque' })).toBeVisible()
    await steps.getByRole('button', { name: 'Conferir título a pagar' }).click()
    const titles = page.getByRole('list', { name: 'Títulos a pagar do recebimento' })
    await expect(titles.getByRole('listitem')).toHaveCount(2)
    await expect(titles).toContainText('CP-2026-00041')
    await expect(titles).toContainText('Em aberto')

    await page.goto('/app/compras/pedidos/po1?aba=financeiro')
    await expect(page.getByRole('list', { name: 'Títulos previstos do pedido' })).toContainText('R$ 201,25')
  })

  test('recebimentos abre em "A receber" com o atraso, "Recebidos" vazio e tudo cabe a 400 px', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    const late = { id: 'po2', number: 'PC-2026-0002', status: 'Sent', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, projectName: null, total: 800,
      expectedDate: iso(-3), daysLate: 3, itemSummary: 'MP-010 Chapa de aço 3 mm', createdAt: '2026-10-20T12:00:00Z' }
    const asked: string[] = []
    await mockApi(page, {
      ...receivingApi({ order: order() }, {}),
      'GET /api/v1/purchasing/purchase-orders': (r: Route) => { asked.push(new URL(r.request().url()).searchParams.get('show') ?? ''); return json(r, page1([late])) },
      'GET /api/v1/purchasing/goods-receipts': (r: Route) => json(r, page1([])),
    })
    await page.goto('/app/compras/recebimentos')
    await expect(page.getByRole('heading', { name: 'Compras: Recebimentos' })).toBeVisible({ timeout: 20_000 })
    await expect(page.getByRole('tab', { name: 'A receber' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByText('Atrasado 3 d').first()).toBeVisible()
    expect(asked).toContain('a-receber')
    await expectNoHorizontalScroll(page)
    await page.getByRole('tab', { name: 'Recebidos' }).click()
    await expect(page.getByText('Nenhum recebimento.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver pedidos a receber' })).toBeVisible()
    await expectNoHorizontalScroll(page)
  })

  test('quem pediu ou aprovou vê "Registrar recebimento" desabilitado com o motivo', async ({ page }) => {
    const reason = 'Quem pediu ou aprovou este pedido não registra o recebimento dele.'
    await mockApi(page, receivingApi({ order: order({ canReceive: false, receiveBlocker: reason }) }, {}))
    await page.goto('/app/compras/pedidos/po1')
    await expect(page.getByRole('button', { name: 'Registrar recebimento' })).toBeDisabled({ timeout: 20_000 })
    await expect(page.getByText(reason)).toBeVisible()
  })
})
