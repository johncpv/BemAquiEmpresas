import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'
import { OPERATOR, saleListItem, salePage, storeModules } from './pdv-api'

/**
 * Marco MVP do PDV, fluxo 5 (PDV-07): Loja → Vendas lista, filtra pelo estado (vai para a URL e para a API) e abre o detalhe da venda.
 * Verificação-chave: o CPF do consumidor aparece mascarado; o documento inteiro só sai pelo "Mostrar" (rota própria, auditada).
 */
const ONLINE = saleListItem({ id: 'sale-1', number: 'C01-000001', total: 18.99 })
const OFFLINE = saleListItem({ id: 'sale-2', number: 'C01-000002', total: 60, offline: true, paymentMethods: ['Dinheiro', 'Pix'], customerDocument: '***.982.247-**' })

const detail = {
  id: OFFLINE.id, number: OFFLINE.number, channel: 'Pos', status: 'Completed', stockStatus: 'Posted', hasDivergence: false, offline: true, paymentDifference: null,
  storeId: 's1', storeName: 'Loja Centro', registerId: 'r1', registerLabel: 'Caixa 01', cashSessionId: 'cs1', operatorId: OPERATOR.userId, operatorName: OPERATOR.displayName,
  businessDate: OFFLINE.businessDate, at: OFFLINE.at, completedAtDevice: OFFLINE.at, receivedAt: OFFLINE.at, subtotal: 60, discountTotal: 0, serviceFee: 0, total: 60,
  customerDocument: '***.982.247-**', cancelledAt: null, cancelReason: null, cancelledByName: null,
  lines: [{ id: 'dl1', lineNumber: 1, description: 'Camiseta', unitCode: 'UN', quantity: 2, listUnitPrice: 30, unitPrice: 30, discount: 0, total: 60, discountReason: null, priceReason: null }],
  payments: [{ id: 'dp1', methodName: 'Dinheiro', amount: 60, changeAmount: 0, status: 'Confirmed', integration: 'Manual', installments: 1, cardBrand: null, nsu: null, authorizationCode: null, confirmedAt: OFFLINE.at, confirmedByName: null }],
  trail: [{ at: OFFLINE.at, kind: 'Synced', title: 'Venda recebida do caixa', detail: 'Feita sem internet', tone: 'info' }],
}

test('Loja → Vendas: filtra pelo estado, abre o detalhe e o CPF vem mascarado', async ({ page }) => {
  const states: (string | null)[] = []
  const reveals: string[] = []
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: storeModules() }),
    'GET /api/v1/pos/stores': (r: Route) => json(r, [{ id: 's1', name: 'Loja Centro', code: 'CENTRO', isActive: true, registers: [{ id: 'r1', number: 1, isActive: true, device: null }] }]),
    'GET /api/v1/finance/payment-methods': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    'GET /api/v1/sales/sales': (r: Route) => {
      const state = new URL(r.request().url()).searchParams.get('State')
      states.push(state)
      return json(r, salePage(state === 'Offline' ? [OFFLINE] : [ONLINE, OFFLINE]))
    },
    'GET /api/v1/sales/sales/*': (r: Route) => json(r, detail),
    'GET /api/v1/sales/sales/*/customer-document': (r: Route) => { reveals.push(r.request().url()); return json(r, { document: '529.982.247-25' }) },
  })

  await page.goto('/app/loja/vendas')
  await expect(page.getByRole('heading', { name: 'Vendas', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: ONLINE.number }).first()).toBeVisible()
  // A lista nunca mostra o CPF, nem mascarado.
  await expect(page.getByText('***.982.247-**')).toHaveCount(0)

  await page.getByLabel('Estado').selectOption({ label: 'Feitas sem internet' })
  await expect(page).toHaveURL(/estado=Offline/)
  await expect(page.getByRole('link', { name: ONLINE.number })).toHaveCount(0)
  expect(states).toContain('Offline')

  await page.getByRole('link', { name: OFFLINE.number }).first().click()
  await expect(page.getByRole('heading', { name: `Venda ${OFFLINE.number}` })).toBeVisible()
  await expect(page.getByText('***.982.247-**')).toBeVisible()
  await expect(page.getByText('529.982.247-25')).toHaveCount(0)
  expect(reveals).toHaveLength(0)

  await page.getByRole('button', { name: 'Mostrar' }).click()
  await expect(page.getByText('529.982.247-25')).toBeVisible()
  expect(reveals).toHaveLength(1)
})
