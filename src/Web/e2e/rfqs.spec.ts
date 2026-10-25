import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'receive']
const iso = (days: number) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

const plate = { id: 'i1', code: 'MP-010', name: 'Chapa de aço 3 mm', type: 'RawMaterial', unitId: 'kg', unitCode: 'KG', units: [{ unitId: 'kg', code: 'KG', isStockUnit: true, toStock: 1 }] }
const bolt = { id: 'i2', code: 'MP-020', name: 'Parafuso M8', type: 'RawMaterial', unitId: 'un', unitCode: 'UN', units: [{ unitId: 'un', code: 'UN', isStockUnit: true, toStock: 1 }] }
const last = { unitPrice: 10.5, approvedAt: '2026-09-01T12:00:00Z', partyName: 'Alfa Aços', orderNumber: 'PC-2026-0003' }

const byItem = page1([
  { itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'KG', openQuantity: 100, nearestNeededBy: iso(10), requestCount: 2, requests: [
    { requestId: 'sc4', number: 'SC-2026-0004', origin: 'Manual', quantity: 60, neededBy: iso(10), projectNumber: null, orderNumber: null, lineId: 'l1' },
    { requestId: 'sc5', number: 'SC-2026-0005', origin: 'BomShortage', quantity: 40, neededBy: iso(12), projectNumber: 'PRJ-2026-0007', orderNumber: 'OS-2026-00041', lineId: 'l3' },
  ] },
  { itemId: 'i2', itemCode: 'MP-020', itemName: 'Parafuso M8', unitCode: 'UN', openQuantity: 10, nearestNeededBy: iso(20), requestCount: 1, requests: [
    { requestId: 'sc4', number: 'SC-2026-0004', origin: 'Manual', quantity: 10, neededBy: iso(20), projectNumber: null, orderNumber: null, lineId: 'l2' },
  ] },
])

const quote = (lineId: string, unitPrice: number, quantity: number, landedCost: number, leadDays: number, isCheapest: boolean) =>
  ({ lineId, unitPrice, supplierUnitPrice: null, supplierUnit: null, availableQuantity: quantity, leadDays, lineTotal: unitPrice * quantity, landedCost, isCheapest })

function supplier(id: string, partyId: string, partyName: string, sequence: number, overrides: Record<string, unknown> = {}) {
  return {
    id, partyId, partyName, sequence, responded: false, declined: false, respondedBy: null, respondedByName: null, respondedAt: null, freight: 0, ipiPct: 0, paymentTermId: 't3',
    paymentTermName: '30/60/90 dias', validUntil: null, notes: null, subtotal: 0, ipiAmount: 0, total: 0, maxLeadDays: null, coversAll: false, isCheapest: false, attachmentRequired: false,
    attachmentsCount: 0, purchaseOrderId: null, purchaseOrderNumber: null, orderSubmittedAt: null, orderSubmitError: null, quotes: [], supplierUnits: [], ...overrides,
  }
}

const responded = { responded: true, respondedBy: 'u1', respondedByName: 'Ana Souza', respondedAt: '2026-10-24T12:00:00Z' }
const alfa = supplier('s1', 'p1', 'Alfa Aços', 1, { ...responded, freight: 300, subtotal: 1500, total: 1800, maxLeadDays: 5, coversAll: true, quotes: [quote('la', 10, 100, 1200, 5, false), quote('lb', 50, 10, 600, 5, false)] })
const beta = supplier('s2', 'p2', 'Beta Metais', 2, { ...responded, ipiPct: 5, subtotal: 1620, ipiAmount: 81, total: 1701, maxLeadDays: 10, coversAll: true, isCheapest: true, quotes: [quote('la', 11, 100, 1155, 10, true), quote('lb', 52, 10, 546, 10, false)] })
const gamaPending = supplier('s3', 'p3', 'Gama Fixadores', 3)
const gama = supplier('s3', 'p3', 'Gama Fixadores', 3, { ...responded, subtotal: 490, total: 490, maxLeadDays: 20, quotes: [quote('lb', 49, 10, 490, 20, true)] })

function rfq(overrides: Record<string, unknown> = {}) {
  return {
    id: 'rfq1', number: 'COT-2026-0001', status: 'Open', notes: null, createdBy: 'u1', createdByName: 'Ana Souza', createdAt: '2026-10-24T11:00:00Z', closedAt: null, closedByName: null,
    cancelReason: null, cancelledAt: null, cancelledByName: null,
    lines: [
      { id: 'la', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'KG', quantity: 100, requests: [{ requestId: 'sc4', number: 'SC-2026-0004', lineId: 'l1', quantity: 60 }, { requestId: 'sc5', number: 'SC-2026-0005', lineId: 'l3', quantity: 40 }],
        cheapestSupplierId: 's2', selectedSupplierId: null, suggestedSupplierId: null, selectionReason: null, selectionNote: null, lastPurchase: last },
      { id: 'lb', sequence: 2, itemId: 'i2', itemCode: 'MP-020', itemName: 'Parafuso M8', unitCode: 'UN', quantity: 10, requests: [{ requestId: 'sc4', number: 'SC-2026-0004', lineId: 'l2', quantity: 10 }],
        cheapestSupplierId: 's2', selectedSupplierId: null, suggestedSupplierId: null, selectionReason: null, selectionNote: null, lastPurchase: null },
    ],
    suppliers: [alfa, beta, gamaPending], quotesCount: 2, cheapestOverallSupplierId: 's2', singleQuoteWarning: null, attachmentLimit: 5000, orders: [], canEdit: true, canClose: true,
    canCancel: true, canGenerateOrders: false, canSubmitOrders: false, version: 3, ...overrides,
  }
}

const allResponded = () => {
  const base = rfq({ suppliers: [alfa, beta, gama], quotesCount: 3, version: 4 })
  base.lines[1] = { ...base.lines[1], cheapestSupplierId: 's3' }
  return base
}

const closed = () => {
  const base = allResponded()
  return {
    ...base, status: 'Closed', closedAt: '2026-10-24T13:00:00Z', closedByName: 'Ana Souza', canEdit: false, canClose: false, canCancel: false, canGenerateOrders: true, version: 5,
    lines: [
      { ...base.lines[0], selectedSupplierId: 's1', suggestedSupplierId: 's2', selectionReason: 'LeadTime' },
      { ...base.lines[1], selectedSupplierId: 's3', suggestedSupplierId: 's3' },
    ],
    orders: [
      { supplierId: 's1', partyId: 'p1', partyName: 'Alfa Aços', lines: 1, total: 1300, order: null },
      { supplierId: 's3', partyId: 'p3', partyName: 'Gama Fixadores', lines: 1, total: 490, order: null },
    ],
  }
}

const withOrders = (statuses: [string, string]) => ({
  ...closed(), canGenerateOrders: false, canSubmitOrders: statuses.includes('Draft'), version: 6,
  orders: [
    { supplierId: 's1', partyId: 'p1', partyName: 'Alfa Aços', lines: 1, total: 1300, order: { id: 'po1', number: 'PC-2026-0010', status: statuses[0], total: 1300 } },
    { supplierId: 's3', partyId: 'p3', partyName: 'Gama Fixadores', lines: 1, total: 490, order: { id: 'po2', number: 'PC-2026-0011', status: statuses[1], total: 490 } },
  ],
})

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

function rfqApi(state: { current: Record<string, unknown> }, posted: Record<string, unknown>) {
  return {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ purchasing: { actions: ALL } as never }) }),
    'GET /api/v1/purchasing/purchase-requests/by-item': (r: Route) => json(r, byItem),
    'GET /api/v1/purchasing/projects': (r: Route) => json(r, []),
    'GET /api/v1/purchasing/rfqs/defaults': (r: Route) => {
      posted.defaults = new URL(r.request().url()).searchParams.getAll('requestLineIds')
      return json(r, {
        lines: [
          { item: plate, quantity: 100, requestLineIds: ['l1', 'l3'], requestNumbers: ['SC-2026-0004', 'SC-2026-0005'], lastPurchase: last },
          { item: bolt, quantity: 10, requestLineIds: ['l2'], requestNumbers: ['SC-2026-0004'], lastPurchase: null },
        ],
        suppliers: [{ id: 'p1', name: 'Alfa Aços', document: null, defaultPaymentTermId: 't3', suggested: true }, { id: 'p2', name: 'Beta Metais', document: null, defaultPaymentTermId: 't3', suggested: true }],
      })
    },
    'GET /api/v1/purchasing/suppliers': (r: Route) => json(r, [{ id: 'p3', name: 'Gama Fixadores', document: '12345678000190', defaultPaymentTermId: 't3' }]),
    'GET /api/v1/purchasing/payment-terms': (r: Route) => json(r, [{ id: 't3', name: '30/60/90 dias', installments: [] }, { id: 't0', name: 'À vista', installments: [] }]),
    'POST /api/v1/purchasing/rfqs': async (r: Route) => {
      posted.create = r.request().postDataJSON()
      posted.createKey = r.request().headers()['idempotency-key']
      state.current = rfq()
      await json(r, state.current, 201)
    },
    'GET /api/v1/purchasing/rfqs/rfq1': (r: Route) => json(r, state.current),
    'PUT /api/v1/purchasing/rfqs/rfq1/suppliers/s3/response': async (r: Route) => {
      posted.response = r.request().postDataJSON()
      state.current = allResponded()
      await json(r, state.current)
    },
    'POST /api/v1/purchasing/rfqs/rfq1/close': async (r: Route) => {
      posted.close = r.request().postDataJSON()
      state.current = closed()
      await json(r, state.current)
    },
    'POST /api/v1/purchasing/rfqs/rfq1/purchase-orders': async (r: Route) => {
      posted.generate = r.request().postDataJSON()
      posted.generateKey = r.request().headers()['idempotency-key']
      state.current = withOrders(['Draft', 'Draft'])
      await json(r, { orders: [], created: 2 })
    },
    'POST /api/v1/purchasing/rfqs/rfq1/purchase-orders/submit': async (r: Route) => {
      posted.submit = true
      state.current = withOrders(['PendingApproval', 'Draft'])
      await json(r, {
        items: [
          { purchaseOrderId: 'po1', number: 'PC-2026-0010', partyName: 'Alfa Aços', total: 1300, sent: true, levels: 1, approverName: 'Proprietário', code: null, message: null },
          { purchaseOrderId: 'po2', number: 'PC-2026-0011', partyName: 'Gama Fixadores', total: 490, sent: false, levels: null, approverName: null, code: 'approval_not_viable', message: 'Nenhuma regra de alçada cobre o pedido. Ajuste as alçadas.' },
        ],
        sent: 1, total: 2, message: 'Pedidos enviados para aprovação (1 de 2)',
      })
    },
    'GET /api/v1/purchasing/rfqs/rfq1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/attachments/Rfq/*': (r: Route) => json(r, []),
  }
}

test.describe('Cotações (CMP-02)', () => {
  test('cotar dois itens com três fornecedores, comparar pelo rádio, concluir, gerar os pedidos e enviar todos', async ({ page }) => {
    const state = { current: rfq() }
    const posted: Record<string, unknown> = {}
    await mockApi(page, rfqApi(state, posted))

    await page.goto('/app/compras/solicitacoes?visao=item')
    const table = page.getByRole('table', { name: 'Itens a comprar' })
    await expect(table.getByText('100 KG')).toBeVisible({ timeout: 20_000 })
    await page.getByRole('checkbox', { name: 'Selecionar MP-010 Chapa de aço 3 mm' }).check()
    await page.getByRole('checkbox', { name: 'Selecionar MP-020 Parafuso M8' }).check()
    await page.getByRole('button', { name: 'Cotar 2 itens' }).click()
    await expect(page).toHaveURL(/\/app\/compras\/cotacoes\/nova\?sc=l1(%2C|,)l3(%2C|,)l2/)

    await expect(page.getByRole('heading', { name: 'Nova cotação' })).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Atende SC-2026-0004, SC-2026-0005')).toBeVisible()
    await expect(page.getByText('Sugerido', { exact: true })).toHaveCount(2)
    await page.getByRole('combobox', { name: /Incluir fornecedor/ }).click()
    await page.getByPlaceholder('Digite para buscar').fill('Gama')
    await page.getByRole('option', { name: /Gama Fixadores/ }).click()
    await page.getByRole('button', { name: 'Criar cotação' }).click()
    await expect(page.getByText('Cotação criada').first()).toBeVisible()
    expect(posted.create).toMatchObject({ requestLineIds: ['l1', 'l3', 'l2'], supplierIds: ['p1', 'p2', 'p3'], lines: [] })
    expect(posted.createKey).toBeTruthy()

    await expect(page.getByRole('heading', { name: /COT-2026-0001/ })).toBeVisible()
    await expect(page.getByText('Lançada por Ana Souza em').first()).toBeVisible()
    const gamaCard = page.getByRole('listitem').filter({ hasText: 'Gama Fixadores' }).filter({ hasText: 'Aguardando resposta' })
    await gamaCard.getByRole('button', { name: 'Registrar resposta' }).click()
    await page.getByRole('textbox', { name: 'Preço unitário de MP-020' }).fill('49')
    await page.getByRole('textbox', { name: 'Prazo em dias de MP-020' }).fill('20')
    await expect(page.getByRole('region', { name: 'Custo total de Gama Fixadores' })).toContainText('R$ 490,00')
    await page.getByRole('button', { name: 'Salvar resposta' }).click()
    await expect(page.getByText('Resposta salva').first()).toBeVisible()
    expect(posted.response).toMatchObject({ paymentTermId: 't3', quotes: [{ lineId: 'lb', unitPrice: 49, inSupplierUnit: false, leadDays: 20 }], version: 3 })

    await page.getByRole('tab', { name: 'Comparativo' }).click()
    await expect(page).toHaveURL(/aba=comparativo/)
    const lineA = page.getByRole('radiogroup', { name: 'Vencedor de MP-010 Chapa de aço 3 mm' })
    await expect(lineA.getByRole('radio', { name: /^Beta Metais/ })).toBeChecked()
    await expect(lineA.getByText('Sugerido')).toBeVisible()
    await expect(page.getByRole('radiogroup', { name: 'Vencedor de MP-020 Parafuso M8' }).getByRole('radio', { name: /^Gama Fixadores/ })).toBeChecked()
    // Pelo teclado: do Beta (sugerido) para o Alfa, que não é o de menor custo: o motivo passa a ser obrigatório.
    await lineA.getByRole('radio', { name: /^Beta Metais/ }).focus()
    await page.keyboard.press('ArrowLeft')
    await expect(lineA.getByRole('radio', { name: /^Alfa Aços/ })).toBeChecked()
    await page.getByRole('combobox', { name: 'Motivo da escolha de MP-010' }).selectOption('LeadTime')

    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await page.getByRole('button', { name: 'Concluir cotação' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Concluir cotação' }).click()
    await expect(page.getByText('Cotação concluída').first()).toBeVisible()
    expect(posted.close).toEqual({
      selections: [{ lineId: 'la', supplierId: 's1', reason: 'LeadTime', note: null }, { lineId: 'lb', supplierId: 's3', reason: null, note: null }], version: 4,
    })

    await expect(page.getByText('Motivo da escolha: Prazo')).toBeVisible()
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Gerar pedidos de compra' }).click()
    const generate = page.getByRole('alertdialog')
    await expect(generate.getByText('Gerar 2 pedidos de compra?')).toBeVisible()
    await expect(generate).toContainText('Alfa Aços: 1 item, R$ 1.300,00')
    await generate.getByRole('button', { name: 'Gerar pedidos de compra' }).click()
    await expect(page.getByText('Pedidos gerados').first()).toBeVisible()
    expect(posted.generate).toEqual({ version: 5 })
    expect(posted.generateKey).toBeTruthy()

    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Enviar todos para aprovação' }).click()
    const submit = page.getByRole('alertdialog')
    await expect(submit.getByText('Enviar 2 pedidos, total R$ 1.790,00, para aprovação?')).toBeVisible()
    await submit.getByRole('button', { name: 'Enviar todos para aprovação' }).click()
    const orders = page.getByRole('region', { name: 'Pedidos desta cotação' })
    await expect(orders.getByText('Pedidos enviados para aprovação (1 de 2)')).toBeVisible()
    await expect(orders.getByText('Não enviado: Nenhuma regra de alçada cobre o pedido. Ajuste as alçadas.')).toBeVisible()
    await expect(orders.getByRole('link', { name: 'Abrir pedido PC-2026-0010' })).toBeVisible()
    expect(posted.submit).toBe(true)
  })

  test('comparativo a 400 px: cartões por fornecedor e a tabela rola sem rolar a página', async ({ page }, info) => {
    const state = { current: allResponded() as Record<string, unknown> }
    await mockApi(page, rfqApi(state, {}))
    await page.goto('/app/compras/cotacoes/rfq1?aba=comparativo')
    const cards = page.getByRole('list', { name: 'Resumo por fornecedor' })
    await expect(cards.getByRole('listitem').filter({ hasText: 'Beta Metais' })).toContainText('R$ 1.701,00', { timeout: 20_000 })
    await expect(cards.getByRole('button', { name: 'Escolher tudo de Alfa Aços' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Comparativo por item' })).toBeVisible()
    await cards.getByRole('button', { name: 'Escolher tudo de Alfa Aços' }).click()
    await expect(page.getByRole('radiogroup', { name: 'Vencedor de MP-020 Parafuso M8' }).getByRole('radio', { name: /^Alfa Aços/ })).toBeChecked()
    await expect(page.getByRole('button', { name: 'Concluir cotação' })).toBeVisible()
    if (info.project.name === 'celular') {
      await expectNoHorizontalScroll(page)
      const box = await page.getByRole('radiogroup', { name: 'Vencedor de MP-010 Chapa de aço 3 mm' }).locator('label').first().boundingBox()
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)
    }
  })

  test('lista vazia oferece cotar os itens das solicitações', async ({ page }) => {
    await mockApi(page, { ...rfqApi({ current: rfq() }, {}), 'GET /api/v1/purchasing/rfqs': (r: Route) => json(r, page1([])) })
    await page.goto('/app/compras/cotacoes')
    await expect(page.getByText('Nenhuma cotação aberta.')).toBeVisible({ timeout: 20_000 })
    await page.getByRole('link', { name: 'Cotar itens das solicitações' }).first().click()
    await expect(page).toHaveURL(/\/app\/compras\/solicitacoes\?visao=item/)
  })
})
