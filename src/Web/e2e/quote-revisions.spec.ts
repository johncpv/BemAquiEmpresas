import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const base = { number: 'ORC-2026-0007', partyId: 'c1', partyName: 'Aurora', contactId: null, siteAddressId: null, deliveryDays: 30, notes: null, pricingMode: 'Markup', sentAt: null,
  createdAt: '2026-10-20T10:00:00Z', validUntil: '2026-11-04', lostReason: null, lostDetail: null, copiedFromNumber: null, parentQuoteId: null }
const item = (sequence: number, quantity: number, unitPrice: number) => ({ id: `i${sequence}`, sequence, kind: 'Equipment', itemId: null, itemName: null, description: `Item ${sequence}`, quantity, unitPrice, priceOverridden: false, total: quantity * unitPrice })
const rev0 = { ...base, id: 'r0', revision: 0, status: 'Superseded', isCurrent: false, items: [item(1, 1, 9000)], totalPrice: 9000, version: 3 }
const rev1 = { ...base, id: 'r1', revision: 1, status: 'Draft', isCurrent: true, items: [item(1, 1, 9500), item(2, 1, 500)], totalPrice: 10000, version: 1 }

test.describe('Revisões do orçamento (COM-02)', () => {
  test('revisão antiga em leitura, aba Revisões e comparação com o que mudou (CA 4, 8 e 9)', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/quotes/r0': (r: Route) => json(r, rev0),
      'GET /api/v1/quotes/r1': (r: Route) => json(r, rev1),
      'GET /api/v1/quotes/r0/revisions/r1': (r: Route) => json(r, rev1),
      'GET /api/v1/quotes/r1/revisions/r0': (r: Route) => json(r, rev0),
      'GET /api/v1/quotes/r0/revisions': (r: Route) => json(r, [
        { id: 'r1', revision: 1, status: 'Draft', isCurrent: true, createdAt: '2026-10-20T11:00:00Z', createdByName: 'Ana', totalPrice: 10000, validUntil: '2026-11-04' },
        { id: 'r0', revision: 0, status: 'Superseded', isCurrent: false, createdAt: '2026-10-20T10:00:00Z', createdByName: 'Ana', totalPrice: 9000, validUntil: '2026-11-04' },
      ]),
      'GET /api/v1/quotes/*/conditions': (r: Route) => json(r, { milestones: [], percentSum: 0, warning: null, validUntil: '2026-11-04', defaultPaymentTermId: null, version: 1 }),
    })
    await page.goto('/app/orcamentos/r0?aba=revisoes')
    await expect(page.getByText('Há uma revisão mais recente deste orçamento.', { exact: false })).toBeVisible()
    await page.getByRole('checkbox', { name: 'Comparar a revisão 1' }).check()
    await page.getByRole('checkbox', { name: 'Comparar a revisão 0' }).check()
    await page.getByRole('link', { name: 'Comparar' }).click()
    const items = page.getByRole('list', { name: 'Itens comparados' })
    await expect(items.getByText('Alterado')).toBeVisible()
    await expect(items.getByText('Incluído')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Totais' })).toContainText('(+11,11%)')
  })
})
