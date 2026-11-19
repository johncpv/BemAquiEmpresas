import { getGetPosCatalogChangesUrl, getGetPosCatalogSnapshotUrl, getPosCatalogChanges, getPosCatalogSnapshot } from '@/api/generated/bemaqui'
import type { PosCatalogChanges, PosCatalogSnapshot, PosSyncItem, PosSyncPaymentMethod, PosSyncStore } from '@/api/generated/model'
import type { CatalogChanges, CatalogSnapshot, CatalogSyncSource } from './catalog-sync'
import { signRequest } from './device-signing'
import type { CatalogBarcode, CatalogChange, CatalogCollection, CatalogData, CatalogPaymentMethod, CatalogPrice, CatalogRecords } from './local-store/catalog-types'
import type { LocalStore } from './local-store/local-store'

/**
 * Fonte real da descida do catálogo (OFF-01 CA 10 e 11): o cliente Orval de `GET /api/v1/pos/sync/snapshot` e
 * `GET /api/v1/pos/sync/changes?cursor=`, com a assinatura do aparelho em cada chamada (esquema `Device`, ADR 0048). O servidor já manda o
 * preço em centavos; aqui só se traduz o formato do servidor (item com os códigos de barras dentro, listas inteiras de operadores e impressoras)
 * para as coleções do `LocalStore`.
 *
 * O delta do servidor traz os itens e as formas de pagamento que mudaram (e os ids removidos) e, sempre inteiras, as listas de operadores e de
 * impressoras. Como o `LocalStore` aplica mudança por registro, o adaptador compara com o que já está no aparelho para gerar as remoções
 * (código de barras que saiu do item, operador que deixou a loja).
 */
export function createCatalogSource(keys: CryptoKeyPair, store: Pick<LocalStore, 'list'>): CatalogSyncSource {
  return {
    async snapshot(options) {
      const url = getGetPosCatalogSnapshotUrl()
      const body = await getPosCatalogSnapshot({ headers: await signRequest(keys, 'GET', url), signal: options?.signal })
      options?.onProgress?.(1)
      return toSnapshot(body)
    },
    async changes(cursor, options) {
      const params = { cursor: Number(cursor) }
      const url = getGetPosCatalogChangesUrl(params)
      const body = await getPosCatalogChanges(params, { headers: await signRequest(keys, 'GET', url), signal: options?.signal })
      return toChanges(body, store)
    },
  }
}

const offlineWindowDays = (s: PosSyncStore) => (s.offlineWindowHours > 0 ? Math.ceil(s.offlineWindowHours / 24) : null)

const barcodeId = (itemId: string, code: string) => `${itemId}:${code}`

function itemRecords(item: PosSyncItem, priceListVersion: number) {
  const record = { id: item.productId, name: item.description, unit: item.unitCode, sku: item.plu, fractional: item.isWeighable }
  const barcodes: CatalogBarcode[] = item.barcodes.map((b) => ({ id: barcodeId(item.productId, b.gtin), code: b.gtin, itemId: item.productId, packQuantity: b.packQuantity }))
  const price: CatalogPrice | null = item.priceCents === null
    ? null
    : { id: item.productId, itemId: item.productId, priceCents: item.priceCents as CatalogPrice['priceCents'], tableVersion: item.priceListVersion ?? priceListVersion }
  return { record, barcodes, price }
}

/** Forma de pagamento como fica no aparelho: sem taxa (nunca desce), com as bandeiras e o teto de parcelas (PDV-03 CA 4). */
const paymentMethodRecord = (m: PosSyncPaymentMethod): CatalogPaymentMethod => ({
  id: m.id, name: m.name, kind: m.type, allowsChange: m.allowsChange, cardBrands: [...(m.cardBrands ?? [])], maxInstallments: m.maxInstallments ?? 1,
})

export function toSnapshot(body: PosCatalogSnapshot): CatalogSnapshot {
  const data: CatalogData = { items: [], barcodes: [], prices: [], paymentMethods: [], operators: [], printers: [] }
  for (const item of body.items) {
    const { record, barcodes, price } = itemRecords(item, body.priceList.version)
    data.items.push(record)
    data.barcodes.push(...barcodes)
    if (price) data.prices.push(price)
  }
  data.paymentMethods = body.paymentMethods.map(paymentMethodRecord)
  data.operators = body.operators.map((o) => ({ id: o.userId, displayName: o.displayName }))
  data.printers = body.printers.items.map((p) => ({ id: p.id, name: p.name, kind: 'printer' }))
  return {
    schemaVersion: body.schemaVersion,
    cursor: String(body.cursor),
    storeId: body.store.id,
    storeName: body.store.name,
    offlineWindowDays: offlineWindowDays(body.store),
    data,
  }
}

const upsert = <K extends CatalogCollection>(collection: K, record: CatalogRecords[K]) => ({ collection, op: 'upsert', record }) as CatalogChange
const remove = (collection: CatalogCollection, id: string) => ({ collection, op: 'delete', id }) as CatalogChange

/** Lista inteira do servidor contra a do aparelho: o que sumiu vira remoção. */
function replaceList<K extends 'operators' | 'printers'>(collection: K, next: CatalogRecords[K][], store: Pick<LocalStore, 'list'>): CatalogChange[] {
  const keep = new Set(next.map((r) => r.id))
  return [
    ...store.list(collection).filter((r) => !keep.has(r.id)).map((r) => remove(collection, r.id)),
    ...next.map((r) => upsert(collection, r)),
  ]
}

export function toChanges(body: PosCatalogChanges, store: Pick<LocalStore, 'list'>): CatalogChanges {
  const changes: CatalogChange[] = []
  const barcodesByItem = new Map<string, string[]>()
  for (const b of store.list('barcodes')) barcodesByItem.set(b.itemId, [...(barcodesByItem.get(b.itemId) ?? []), b.id])

  for (const item of body.items) {
    const { record, barcodes, price } = itemRecords(item, body.priceList.version)
    changes.push(upsert('items', record))
    const keep = new Set(barcodes.map((b) => b.id))
    for (const old of barcodesByItem.get(item.productId) ?? []) if (!keep.has(old)) changes.push(remove('barcodes', old))
    for (const b of barcodes) changes.push(upsert('barcodes', b))
    changes.push(price ? upsert('prices', price) : remove('prices', item.productId))
  }
  for (const id of body.removedItemIds) {
    changes.push(remove('items', id), remove('prices', id))
    for (const old of barcodesByItem.get(id) ?? []) changes.push(remove('barcodes', old))
  }
  for (const m of body.paymentMethods) changes.push(upsert('paymentMethods', paymentMethodRecord(m)))
  for (const id of body.removedPaymentMethodIds) changes.push(remove('paymentMethods', id))
  changes.push(...replaceList('operators', body.operators.map((o) => ({ id: o.userId, displayName: o.displayName })), store))
  changes.push(...replaceList('printers', body.printers.items.map((p) => ({ id: p.id, name: p.name, kind: 'printer' })), store))

  return {
    schemaVersion: body.schemaVersion,
    cursor: String(body.cursor),
    offlineWindowDays: offlineWindowDays(body.store),
    hasMore: body.hasMore,
    changes,
  }
}
