import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const iso = (days: number) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}

const defaults = (overrides: Record<string, unknown> = {}) => ({
  invoiceId: 'nf1', invoiceNumber: 4521, series: 1, supplierName: 'Gama Aços', issuedOn: iso(-2), stage: 'Checked', goodsReceiptId: null, invoiceVersion: 3,
  orderId: 'po1', orderNumber: 'PC-2026-0001', orderVersion: 9, projectNumber: null, today: iso(0), defaultLocationId: 'alm',
  locations: [{ id: 'alm', code: 'ALM', name: 'Almoxarifado', isDefault: true }],
  lines: [
    { poLineId: 'pl1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', tracksLot: true, quantity: 10, open: 10, maxQuantity: 10, orderPrice: 50,
      invoicePrice: 50, priceDiffers: false, suggestedLot: 'L-NF-77', invoiceItems: [1] },
  ],
  noStock: [], freight: 20, insurance: 0, otherExpenses: 0, ipiAmount: 25, freightMax: 20, ipiMax: 25, total: 545,
  duplicates: [{ number: '001', dueDate: iso(28), amount: 272.5 }, { number: '002', dueDate: iso(58), amount: 272.5 }],
  creditRule: 'Sem crédito de impostos (a confirmar com a contadora)', creditConfirmed: false, priceBlocked: false, soleOwner: false, blocker: null, blockerCode: null, ...overrides,
})

const preview = {
  receipt: {
    receivedOn: iso(0), subtotal: 500, freight: 20, ipiAmount: 25, amount: 545, freightMax: 20, ipiMax: 25, ceiling: 545, receivedAmountAfter: 545, orderCompletes: true, soleOwner: false,
    plannedBefore: 545, plannedAfter: 0, insurance: 0, otherExpenses: 0, taxCredit: 0,
    lines: [{ poLineId: 'pl1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', quantity: 10, lineAmount: 500, freightShare: 20, ipiShare: 25, unitCost: 54.5,
      averageCostBefore: 50, averageCostAfter: 52.25, insuranceShare: 0, otherShare: 0, taxCreditShare: 0 }],
    installments: [{ number: 1, dueDate: iso(28), amount: 272.5 }, { number: 2, dueDate: iso(58), amount: 272.5 }],
  },
  noStock: [], creditRule: 'Sem crédito de impostos (a confirmar com a contadora)', fromDuplicates: true, duplicatesMismatch: false, invoiceTotal: 545,
}

const permissions = (r: Route) => json(r, {
  permissions: [], isOwner: false, version: '1',
  modules: allModules({ purchasing: { actions: ['view', 'receive'] } as never, inventory: { actions: ['view'] } as never, fiscal: { actions: ['view'] } as never, finance: { actions: ['view'] } as never }),
})

/** FIS-04b: o almoxarife recebe pela nota conferida; o formulário vem preenchido e o diálogo mostra os títulos pelas duplicatas. */
test('recebe pela nota com o lote sugerido e os títulos pelas duplicatas', async ({ page }) => {
  const posted: Record<string, unknown> = {}
  await mockApi(page, {
    'GET /api/v1/me/permissions': permissions,
    'GET /api/v1/purchasing/incoming-invoices/nf1/receipt-defaults': (r) => json(r, defaults()),
    'POST /api/v1/purchasing/incoming-invoices/nf1/receipt/preview': (r) => json(r, preview),
    'POST /api/v1/purchasing/incoming-invoices/nf1/receipt': async (r) => {
      posted.body = r.request().postDataJSON()
      posted.key = r.request().headers()['idempotency-key']
      await json(r, { id: 'rec1' }, 201)
    },
    'GET /api/v1/purchasing/goods-receipts/rec1': (r) => json(r, { id: 'rec1', number: 'REC-2026-0001', status: 'Completed', purchaseOrderId: 'po1', purchaseOrderNumber: 'PC-2026-0001',
      partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, receivedOn: iso(0), subtotal: 500, freight: 20, ipiAmount: 25, amount: 545, notes: null, receivedBy: 'u1',
      receivedByName: 'Ana Souza', soleOwner: false, createdAt: '2026-10-31T12:00:00Z', reversedAt: null, reversedByName: null, reversalReason: null, servedOrders: [], canReverse: false,
      version: 1, origin: 'Invoice', incomingInvoiceId: 'nf1', insurance: 0, otherExpenses: 0, taxCredit: 0, lines: [] }),
  })

  await page.goto('/app/fiscal/entrada/nf1/recebimento')
  await expect(page.getByText('Valores da NF-e 4521 · Gama Aços')).toBeVisible()
  await expect(page.getByLabel(/^Lote/)).toHaveValue('L-NF-77')
  await expect(page.getByText('lote do XML da nota')).toBeVisible()
  await page.getByRole('button', { name: 'Receber pela nota' }).click()
  const dialog = page.getByRole('alertdialog', { name: 'Receber a NF-e 4521?' })
  await expect(dialog).toContainText('Regra usada: Sem crédito de impostos (a confirmar com a contadora)')
  await expect(dialog.getByRole('table', { name: 'Títulos a pagar do recebimento' }).getByRole('row')).toHaveCount(3)
  await dialog.getByRole('button', { name: 'Receber pela nota' }).click()
  await expect(page).toHaveURL(/\/app\/compras\/recebimentos\/rec1/)
  expect(posted.key).toBeTruthy()
  expect((posted.body as { lines: { lotNumber: string }[] }).lines[0].lotNumber).toBe('L-NF-77')
  await expect(page.getByRole('link', { name: 'Pela NF-e de entrada' })).toBeVisible()
})

/** FIS-04b CA 3: preço da nota diferente do pedido bloqueia o recebimento e oferece "Pedir correção de preço". */
test('preço diferente bloqueia e pede a correção a quem aprova compras', async ({ page }) => {
  let asked = false
  await mockApi(page, {
    'GET /api/v1/me/permissions': permissions,
    'GET /api/v1/purchasing/incoming-invoices/nf1/receipt-defaults': (r) => json(r, defaults({
      priceBlocked: true, blocker: 'O preço da nota é diferente do pedido. Peça a correção do preço a quem aprova compras.', blockerCode: 'incoming_invoice_price_difference',
      lines: [{ ...defaults().lines[0], invoicePrice: 52, priceDiffers: true }],
    })),
    'POST /api/v1/purchasing/incoming-invoices/nf1/price-correction-request': async (r) => {
      asked = true
      await json(r, { notified: 2, lines: ['pl1'] })
    },
  })

  await page.goto('/app/fiscal/entrada/nf1/recebimento')
  await expect(page.getByText('Preço diferente do pedido')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Receber pela nota' })).toBeDisabled()
  await page.getByRole('button', { name: 'Pedir correção de preço' }).click()
  await expect(page.getByText('Pedido de correção enviado a 2 aprovadores')).toBeVisible()
  expect(asked).toBe(true)
})
