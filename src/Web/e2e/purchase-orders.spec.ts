import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'pay']
const iso = (days: number) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

const plate = { id: 'i1', code: 'MP-010', name: 'Chapa de aço 3 mm', type: 'RawMaterial', unitId: 'kg', unitCode: 'KG', units: [{ unitId: 'kg', code: 'KG', isStockUnit: true, toStock: 1 }] }
const scLine = { id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'KG', quantity: 100, enteredQuantity: null, enteredUnitCode: null, neededBy: iso(10),
  status: 'Open', productionOrderId: null, orderNumber: null, bomLineId: null, manuallyEdited: false, calculatedOnOnboarding: false, cancelReason: null, version: 2 }
const request = {
  id: 'sc1', number: 'SC-2026-0004', origin: 'Manual', status: 'Open', projectId: null, projectNumber: null, projectName: null, productionOrderId: null, orderNumber: null, notes: null,
  requestedBy: 'u1', requestedByName: 'Ana Souza', createdAt: '2026-10-23T12:00:00Z', updatedAt: '2026-10-23T12:00:00Z', cancelReason: null, cancelledAt: null, cancelledByName: null,
  nearestNeededBy: iso(10), canEdit: true, canCancel: true, lines: [scLine], version: 4,
}
const installments = [
  { number: 1, estimatedDueDate: iso(50), amount: 400 }, { number: 2, estimatedDueDate: iso(80), amount: 400 }, { number: 3, estimatedDueDate: iso(110), amount: 400 },
]

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: 'po1', number: 'PC-2026-0001', status: 'Draft', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, projectName: null, paymentTermId: 't3',
    paymentTermName: '30/60/90 dias', expectedDate: iso(20), daysLate: null, subtotal: 1200, freight: 0, ipiPct: 0, ipiAmount: 0, total: 1200, notes: null, rfqId: null, quotesCount: null,
    singleQuoteWarning: null, requestedBy: 'u2', requestedByName: 'Bruno Compras', createdAt: '2026-10-23T12:00:00Z', submittedAt: null, approvedAt: null, attempt: 0, referenceAmount: null,
    splitWarning: null, selfApproved: false, changedAfterSent: false, rejectionComment: null, cancelReason: null, cancelledAt: null, cancelledByName: null, quantityReceived: 0,
    lines: [{ id: 'pl1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'KG', quantity: 100, enteredQuantity: null, enteredUnitCode: null, unitPrice: 12,
      lineTotal: 1200, costType: 'Material', quantityReceived: 0, requestLineIds: ['l1'], requests: [{ requestId: 'sc1', number: 'SC-2026-0004', lineId: 'l1' }] }],
    levels: [], installments, isRequester: false, canEdit: true, canSubmit: true, canDecide: false, canCancel: true, decideBlocker: null, version: 7, ...overrides,
  }
}

const pendingLevel = { attempt: 1, level: 1, ruleId: 'r1', approverName: 'Proprietário', referenceAmount: 1200, splitWarning: null, status: 'Pending' }

function cashFlow() {
  const periods = Array.from({ length: 120 }, (_, i) => ({ start: iso(i), end: iso(i), inflow: 0, outflow: 0, balance: 10_000, projected: true, containsToday: i === 0, overdueInflow: 0, overdueOutflow: 0 }))
  return { today: iso(0), from: iso(0), to: iso(119), view: 'daily', bankAccountId: null, currentBalance: 10_000, consolidated: true, periods, lowest: null, firstNegative: null, undatedReceivables: { count: 0, total: 0 } }
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

/** Estado do pedido simulado: cada ação muda o que o GET devolve. */
function purchasingApi(state: { current: Record<string, unknown> }, posted: Record<string, unknown>) {
  return {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ purchasing: { actions: ALL } as never, finance: { actions: ALL } as never }) }),
    'GET /api/v1/purchasing/purchase-requests/sc1': (r: Route) => json(r, request),
    'GET /api/v1/purchasing/purchase-requests/sc1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/purchasing/purchase-orders/defaults': (r: Route) => json(r, {
      partyId: 's1', partyName: 'Gama Aços', partySuggested: true, paymentTermId: 't3', paymentTermName: '30/60/90 dias', expectedDate: iso(20), leadDays: 20, projectId: null, projectNumber: null,
      projectName: null, lines: [{ item: plate, quantity: 100, unitPrice: 12, priceSuggested: true, priceSource: 'Último preço pago', requestLineIds: ['l1'], requestNumbers: ['SC-2026-0004'] }],
    }),
    'GET /api/v1/purchasing/payment-terms': (r: Route) => json(r, [{ id: 't3', name: '30/60/90 dias', installments: [] }, { id: 't0', name: 'À vista', installments: [] }]),
    'GET /api/v1/purchasing/suppliers': (r: Route) => json(r, [{ id: 's1', name: 'Gama Aços', document: '12345678000190', defaultPaymentTermId: 't3' }]),
    'GET /api/v1/purchasing/projects': (r: Route) => json(r, []),
    'GET /api/v1/purchasing/items': (r: Route) => json(r, [plate]),
    'POST /api/v1/purchasing/purchase-orders': async (r: Route) => {
      posted.create = r.request().postDataJSON()
      posted.key = r.request().headers()['idempotency-key']
      state.current = order()
      await json(r, state.current, 201)
    },
    'GET /api/v1/purchasing/purchase-orders/po1': (r: Route) => json(r, state.current),
    'POST /api/v1/purchasing/purchase-orders/po1/submit': async (r: Route) => {
      posted.submit = r.request().postDataJSON()
      state.current = order({ status: 'PendingApproval', attempt: 1, referenceAmount: 1200, submittedAt: '2026-10-23T13:00:00Z', levels: [pendingLevel], canSubmit: false, version: 8 })
      await json(r, state.current)
    },
    'POST /api/v1/purchasing/purchase-orders/po1/approve': async (r: Route) => {
      posted.approve = r.request().postDataJSON()
      state.current = order({ status: 'Approved', attempt: 1, referenceAmount: 1200, approvedAt: '2026-10-23T14:00:00Z', levels: [{ ...pendingLevel, status: 'Approved' }], canSubmit: false, version: 9 })
      await json(r, state.current)
    },
    'GET /api/v1/purchasing/purchase-orders/po1/decisions': (r: Route) => json(r, { levels: [pendingLevel], events: [] }),
    'GET /api/v1/purchasing/purchase-orders/po1/payables': (r: Route) => json(r, installments.map((i) => ({
      id: `cp${i.number}`, number: `CP-2026-0000${i.number}`, installment: i.number, status: 'Planned', amount: i.amount, dueDate: i.estimatedDueDate, isEstimate: true, paymentOrderId: null,
    }))),
    'GET /api/v1/purchasing/purchase-orders/po1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/attachments/PurchaseOrder/po1': (r: Route) => json(r, []),
    'GET /api/v1/finance/cash-flow': (r: Route) => json(r, cashFlow()),
    'GET /api/v1/finance/cash-flow/day': (r: Route) => json(r, { date: iso(50), projected: true, items: [
      { kind: 'Payable', id: 'cp1', number: 'CP-2026-00001', description: 'Pedido PC-2026-0001 — parcela 1/3', partyName: 'Gama Aços', amount: -400, dueDate: iso(50), overdue: false, estimate: true, link: '/app/financeiro/pagar/cp1' },
    ] }),
    'GET /api/v1/finance/bank-accounts': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
  }
}

test.describe('Pedidos de compra (CMP-03a)', () => {
  test('da SC ao pedido: criar, enviar, aprovar pelo link do e-mail depois do login e ver as parcelas Estimado no caixa', async ({ page }) => {
    const state = { current: order() }
    const posted: Record<string, unknown> = {}
    await mockApi(page, purchasingApi(state, posted))
    await page.goto('/app/compras/solicitacoes/sc1')
    await page.getByRole('button', { name: 'Pedir direto ao último fornecedor' }).click()
    await expect(page).toHaveURL(/\/app\/compras\/pedidos\/novo\?sc=l1/)
    await expect(page.getByRole('heading', { name: 'Novo pedido de compra' })).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Sugerido', { exact: true })).toHaveCount(2)
    await expect(page.getByText('Atende SC-2026-0004')).toBeVisible()
    const totals = page.getByRole('region', { name: 'Totais do pedido' })
    await expect(totals).toContainText('R$ 1.200,00')
    await page.getByRole('textbox', { name: 'Preço unitário da linha 1' }).fill('12,5')
    await expect(totals).toContainText('R$ 1.250,00')
    await page.getByRole('button', { name: 'Criar pedido' }).click()
    await expect(page.getByText('Pedido criado').first()).toBeVisible()
    expect(posted.create).toMatchObject({ partyId: 's1', paymentTermId: 't3', expectedDate: iso(20), lines: [{ itemId: 'i1', quantity: 100, unitPrice: 12.5, requestLineIds: ['l1'] }] })
    expect(posted.key).toBeTruthy()

    await expect(page.getByRole('heading', { name: 'PC-2026-0001 — Gama Aços' })).toBeVisible()
    await page.getByRole('button', { name: 'Enviar para aprovação' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Enviar para aprovação' }).click()
    await expect(page.getByText('Pedido enviado para aprovação').first()).toBeVisible()
    expect(posted.submit).toEqual({ version: 7 })

    // O aprovador abre o link do e-mail sem sessão: entra (com o código) e volta ao pedido com o diálogo de aprovação aberto.
    state.current = order({ status: 'PendingApproval', attempt: 1, referenceAmount: 1200, levels: [pendingLevel], canSubmit: false, canDecide: true, canEdit: true, version: 8 })
    await mockApi(page, {
      ...purchasingApi(state, posted),
      'POST /api/v1/auth/login': (r: Route) => json(r, { accessToken: null, expiresAt: null, twoFactor: { challenge: 'c1', purpose: 'verify', expiresAt: '2030-01-01T00:00:00Z' } }),
      'POST /api/v1/auth/2fa/verify': (r: Route) => json(r, { accessToken: 'tok-aprovador', expiresAt: '2030-01-01T00:00:00Z' }),
    }, { session: false })
    await page.goto('/app/compras/pedidos/po1?acao=aprovar')
    await expect(page).toHaveURL(/\/entrar\?volta=/)
    await page.getByLabel('E-mail').fill('dono@konebras.com.br')
    await page.getByLabel('Senha').fill('uma frase longa de teste')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.getByLabel('Código do app autenticador').fill('123456')
    await page.getByRole('button', { name: 'Confirmar' }).click()
    await expect(page).toHaveURL(/\/app\/compras\/pedidos\/po1/)
    const dialog = page.getByRole('alertdialog', { name: 'Aprovar o PC-2026-0001?' })
    await expect(dialog).toBeVisible({ timeout: 20_000 })
    const impact = dialog.getByRole('group', { name: 'Impacto' })
    await expect(impact.getByRole('table', { name: 'Parcelas previstas no fluxo de caixa' }).getByRole('row')).toHaveCount(4)
    await expect(impact).toContainText('R$ 9.600,00', { timeout: 15_000 })
    await dialog.getByRole('button', { name: 'Aprovar pedido' }).click()
    await expect(page.getByText('Pedido aprovado. Previsão lançada no caixa.').first()).toBeVisible()
    expect(posted.approve).toEqual({ comment: null, version: 8 })

    await page.getByRole('tab', { name: 'Financeiro' }).click()
    const titles = page.getByRole('list', { name: 'Títulos previstos do pedido' })
    await expect(titles.getByText('Estimado', { exact: true })).toHaveCount(3)
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Ver previsão no fluxo de caixa' }).click()
    await expect(page).toHaveURL(/\/app\/financeiro\/fluxo-de-caixa/)
  })

  test('lista: Aguardando você, atraso, 400 px, vazio e erro', async ({ page }, info) => {
    let mode: 'list' | 'empty' | 'error' = 'list'
    const summary = (id: string, number: string, status: string, daysLate: number | null) => ({
      id, number, status, partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, projectName: null, total: 1200, expectedDate: iso(daysLate ? -daysLate : 20), daysLate,
      itemSummary: 'MP-010 Chapa de aço 3 mm', createdAt: '2026-10-23T12:00:00Z',
    })
    let show: string | null = null
    await mockApi(page, {
      ...purchasingApi({ current: order() }, {}),
      'GET /api/v1/purchasing/purchase-orders': (r: Route) => {
        show = new URL(r.request().url()).searchParams.get('show')
        if (mode === 'error') return json(r, { title: 'Erro', status: 500 }, 500)
        return json(r, page1(mode === 'empty' ? [] : [summary('po1', 'PC-2026-0001', 'PendingApproval', null), summary('po2', 'PC-2026-0002', 'Approved', 4)]))
      },
    })
    await page.goto('/app/compras/pedidos')
    await expect(page.getByRole('heading', { name: 'Compras: Pedidos' })).toBeVisible()
    await expect(page.getByText('Mostrando: Aguardando você')).toBeVisible()
    expect(show).toBe('aguardando-voce')
    const table = page.getByRole('table', { name: 'Pedidos de compra' })
    await expect(table.getByRole('link', { name: 'PC-2026-0002' })).toBeVisible()
    await expect(table.getByText('Aguardando aprovação')).toBeVisible()
    if (info.project.name === 'celular') {
      await expect(table.getByRole('columnheader', { name: 'Destino' })).toBeHidden()
      await expect(table.getByRole('columnheader', { name: 'Total' })).toBeVisible()
      await table.getByRole('button', { name: /detalhes de PC-2026-0002/ }).click()
      await expect(page.getByText(/MP-010 Chapa de aço 3 mm · Estoque geral · previsão .* · Atrasado 4 d/)).toBeVisible()
      await expectNoHorizontalScroll(page)
    } else {
      await expect(table.getByText('Atrasado 4 d')).toBeVisible()
    }

    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    mode = 'error'
    await page.reload()
    await expect(page.getByRole('button', { name: 'Tentar de novo' })).toBeVisible({ timeout: 15_000 })
    mode = 'empty'
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByText('Nenhum pedido.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver solicitações' }).last()).toBeVisible()
  })

  test('detalhe a 400 px: itens em cartões, cancelar com o impacto nos previstos e reprovado em rascunho', async ({ page }, info) => {
    const state = { current: order({ status: 'Approved', attempt: 1, levels: [{ ...pendingLevel, status: 'Approved' }], canSubmit: false, approvedAt: '2026-10-23T14:00:00Z' }) }
    let cancelled: unknown
    await mockApi(page, {
      ...purchasingApi(state, {}),
      'POST /api/v1/purchasing/purchase-orders/po1/cancel': async (r: Route) => {
        cancelled = r.request().postDataJSON()
        state.current = order({ status: 'Cancelled', cancelReason: 'Fornecedor sem estoque', cancelledByName: 'Bruno Compras', cancelledAt: '2026-10-23T15:00:00Z', canEdit: false, canCancel: false, canSubmit: false })
        await json(r, state.current)
      },
    })
    await page.goto('/app/compras/pedidos/po1')
    await expect(page.getByRole('list', { name: 'Itens do pedido' }).getByText('Chapa de aço 3 mm')).toBeVisible()
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
    await page.getByRole('button', { name: 'Cancelar pedido' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Cancela 3 títulos previstos de R$ 1.200,00 no fluxo de caixa.')).toBeVisible()
    await dialog.getByLabel('Motivo').fill('Fornecedor sem estoque')
    await dialog.getByRole('button', { name: 'Cancelar pedido' }).click()
    await expect(page.getByText('Pedido cancelado').first()).toBeVisible()
    expect(cancelled).toEqual({ reason: 'Fornecedor sem estoque', version: 7 })
    await expect(page.getByText(/Pedido cancelado por Bruno Compras/)).toBeVisible()

    state.current = order({ rejectionComment: 'Preço acima da tabela.', attempt: 1 })
    await page.reload()
    await expect(page.getByText('Reprovado: Preço acima da tabela. Ajuste e envie de novo para aprovação.')).toBeVisible()
  })

  test('módulo não liberado: tela padrão com o cadeado', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ purchasing: { released: false } }) }),
    })
    await page.goto('/app/compras/pedidos')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })
})
