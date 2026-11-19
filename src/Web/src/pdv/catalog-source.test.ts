import { describe, expect, it } from 'vitest'
import type { PosCatalogChanges, PosCatalogSnapshot, PosSyncItem, PosSyncStore } from '@/api/generated/model'
import { toChanges, toSnapshot } from './catalog-source'
import type { CatalogCollection, CatalogRecords } from './local-store/catalog-types'

const store: PosSyncStore = { id: 's1', code: 'L1', name: 'Loja Centro', registerId: 'r1', registerNumber: 1, offlineWindowHours: 168, cashBreakToleranceCents: 500, exchangeDays: 30, cashDropAlertCents: 100000 }
const item = (id: string, gtins: string[], priceCents: number | null = 1990): PosSyncItem => ({
  productId: id, parentProductId: null, description: `Produto ${id}`, unitCode: 'UN', isWeighable: false, plu: null,
  barcodes: gtins.map((gtin) => ({ gtin, packQuantity: 1 })), priceCents, priceListVersion: 3, changeSeq: 1,
})

const local = (data: Partial<{ [K in CatalogCollection]: CatalogRecords[K][] }>) => ({
  list: <K extends CatalogCollection>(c: K) => (data[c] ?? []) as CatalogRecords[K][],
})

describe('catalog-source', () => {
  it('traduz a foto do servidor para as coleções do aparelho', () => {
    const body: PosCatalogSnapshot = {
      schemaVersion: 1, collections: [], cursor: 42, generatedAt: '', store, priceList: { id: 'p', version: 3 },
      items: [item('a', ['789', '790']), item('b', [], null)],
      paymentMethods: [
        { id: 'm', name: 'Pix', type: 'Pix' as never, allowsChange: false, nfePaymentCode: '17', confirmed: true, cardBrands: [], maxInstallments: 1, changeSeq: 1 },
        { id: 'c', name: 'Crédito', type: 'CreditInstallments' as never, allowsChange: false, nfePaymentCode: '03', confirmed: true, cardBrands: ['Visa', 'Elo'], maxInstallments: 12, changeSeq: 2 },
      ],
      operators: [{ userId: 'u', displayName: 'Ana S.' }], printers: { version: 1, items: [] },
    }
    const snap = toSnapshot(body)
    expect(snap.cursor).toBe('42')
    expect(snap.offlineWindowDays).toBe(7)
    expect(snap.data.items.map((i) => i.id)).toEqual(['a', 'b'])
    expect(snap.data.barcodes.map((b) => b.code)).toEqual(['789', '790'])
    expect(snap.data.prices).toEqual([{ id: 'a', itemId: 'a', priceCents: 1990, tableVersion: 3 }])
    expect(snap.data.operators).toEqual([{ id: 'u', displayName: 'Ana S.' }])
    // PDV-03 CA 4: bandeiras e parcelas descem; taxa nunca.
    expect(snap.data.paymentMethods).toEqual([
      { id: 'm', name: 'Pix', kind: 'Pix', allowsChange: false, cardBrands: [], maxInstallments: 1 },
      { id: 'c', name: 'Crédito', kind: 'CreditInstallments', allowsChange: false, cardBrands: ['Visa', 'Elo'], maxInstallments: 12 },
    ])
  })

  it('no delta, remove o código de barras que saiu, o item removido e o operador que deixou a loja', () => {
    const body: PosCatalogChanges = {
      schemaVersion: 1, cursor: 50, hasMore: false, generatedAt: '', store, priceList: { id: 'p', version: 3 },
      items: [item('a', ['789'])], removedItemIds: ['b'], paymentMethods: [], removedPaymentMethodIds: ['m'],
      operators: [{ userId: 'u2', displayName: 'Bia C.' }], printers: { version: 1, items: [] },
    }
    const out = toChanges(body, local({
      barcodes: [{ id: 'a:789', code: '789', itemId: 'a' }, { id: 'a:790', code: '790', itemId: 'a' }, { id: 'b:111', code: '111', itemId: 'b' }],
      operators: [{ id: 'u', displayName: 'Ana S.' }],
    }))
    const deletes = out.changes.filter((c) => c.op === 'delete').map((c) => `${c.collection}:${'id' in c ? c.id : ''}`)
    expect(deletes).toEqual(expect.arrayContaining(['barcodes:a:790', 'items:b', 'prices:b', 'barcodes:b:111', 'paymentMethods:m', 'operators:u']))
    expect(deletes).not.toContain('barcodes:a:789')
    expect(out.cursor).toBe('50')
  })
})
