import { expect, test } from '@playwright/test'
import { json } from './mock-api'
import { COFFEE, enterWithPin, mockPdv, openRegister, registerDevice, scan } from './pdv-api'

/**
 * Marco MVP do PDV, fluxo 4 (PDV-06): devolução parcial (1 de 2 camisetas) em vale-troca com troca, e o vale usado na venda nova, com o resto em
 * dinheiro. Verificação-chave: a devolução pede só 1 unidade e o vale é baixado pelo valor dele na venda nova.
 */
const SALE = {
  saleId: 'v7', number: 'C01-000007', completedAt: '2026-11-20T15:00:00Z', businessDate: '2026-11-20', total: 30, status: 'Completed', deadline: '2026-12-20',
  late: false, lateMessage: null, cardPayments: [],
  lines: [{ saleLineId: 'l1', description: 'Camiseta', unitCode: 'UN', sold: 2, returned: 0, returnable: 2, lineTotal: 30, unitAmount: 15 }],
}
const CREDIT = { id: 'vt1', code: 'VT-ABCD-EFGH-JKMN', codeLast4: 'JKMN', amount: 15, balance: 15, expiresOn: '2027-02-22' }
const RETURN = {
  id: 'dev1', number: 'DEV-2026-0001', saleId: SALE.saleId, saleNumber: SALE.number, status: 'Requested', refundKind: 'StoreCredit', total: 15, late: false,
  deadline: SALE.deadline, lateMessage: null, needsSupervisor: false, isExchange: true, cardNsu: null, requestedAt: new Date().toISOString(), completedAt: null,
  lines: [{ id: 'x1', saleLineId: 'l1', description: 'Camiseta', quantity: 1, amount: 15, destination: 'Restock', reason: 'Tamanho errado', stockNote: null }],
  storeCredit: null,
}

test('devolução parcial em vale-troca e o vale usado na venda nova', async ({ page }) => {
  const requests: Record<string, unknown>[] = []
  const redeems: Record<string, unknown>[] = []
  await mockPdv(page, {
    overrides: {
      'POST /api/v1/pos/sales/lookup': (r) => json(r, { sales: [SALE], cashSessionOpen: true }),
      'POST /api/v1/pos/returns': (r) => { requests.push(r.request().postDataJSON() as Record<string, unknown>); return json(r, RETURN) },
      'POST /api/v1/pos/returns/*/approve': (r) => json(r, { ...RETURN, status: 'Completed', completedAt: new Date().toISOString(), storeCredit: CREDIT }),
      'POST /api/v1/pos/store-credits/lookup': (r) => json(r, { id: CREDIT.id, codeLast4: 'JKMN', amount: 15, balance: 15, expiresOn: CREDIT.expiresOn, usable: true, problem: null }),
      'POST /api/v1/pos/store-credits/redeem': (r) => {
        const body = r.request().postDataJSON() as Record<string, unknown>
        redeems.push(body)
        return json(r, { storeCreditId: CREDIT.id, tenderId: body.tenderId, amount: 15, balance: 0, expiresOn: CREDIT.expiresOn })
      },
    },
  })
  await registerDevice(page)
  await enterWithPin(page)
  await openRegister(page, '10000')

  await page.getByRole('button', { name: 'Devolução' }).click()
  await page.getByLabel('Número do cupom ou CPF do cliente').fill(SALE.number)
  await page.getByRole('button', { name: 'Buscar' }).click()
  await page.getByRole('button', { name: 'Mais Camiseta' }).click()
  await page.getByRole('button', { name: 'Tamanho errado' }).click()
  await page.getByLabel(/Troca: o cliente já vai levar outro produto/).check()
  await expect(page.getByRole('button', { name: 'Vale-troca', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await page.getByRole('button', { name: 'Concluir devolução' }).click()
  await expect(page.getByText('Devolução DEV-2026-0001 concluída')).toBeVisible()
  await expect(page.getByText(CREDIT.code)).toBeVisible()
  expect(requests[0]).toMatchObject({ saleId: SALE.saleId, refundKind: 'StoreCredit', exchange: true, lines: [{ saleLineId: 'l1', quantity: 1, destination: 'Restock', reason: 'Tamanho errado' }] })

  // Venda nova com o vale: R$ 15,00 do vale e o resto (R$ 3,99) em dinheiro.
  await page.getByRole('button', { name: 'Venda nova com o vale' }).click()
  await scan(page, COFFEE.gtin)
  await page.getByRole('button', { name: /Pagar/ }).click()
  const methods = page.getByRole('group', { name: 'Escolha a forma' })
  await methods.getByRole('button', { name: /Vale-troca/ }).click()
  await expect(page.getByLabel('Código do vale')).toHaveValue(CREDIT.code)
  await page.getByRole('button', { name: 'Consultar vale' }).click()
  await page.getByRole('button', { name: 'Usar o vale' }).click()
  await expect(page.getByLabel('Falta: R$ 3,99')).toBeVisible()
  await methods.getByRole('button', { name: /Dinheiro/ }).click()
  await page.getByLabel('Valor recebido').fill('3,99')
  await page.getByRole('button', { name: 'Confirmar recebimento' }).click()
  await expect(page.getByRole('heading', { name: 'Venda concluída' })).toBeVisible()
  expect(redeems).toEqual([expect.objectContaining({ code: CREDIT.code, amount: 15 })])
})
