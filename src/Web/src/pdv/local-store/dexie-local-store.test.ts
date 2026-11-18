import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { isUuidV7 } from '@/pdv/uuid-v7'
import { type CatalogData, emptyCatalog } from './catalog-types'
import { CHUNKS, DexieLocalStore, OVERLAY_COMPACT_AT } from './dexie-local-store'
import { ReadOnlyStoreError } from './local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from './writer-lock'

/** OFF-01 CA 6 a 8: a porta LocalStore (IndexedDB com Dexie, sobre o IndexedDB falso) com leitura, gravação e a segunda aba. */
function catalog(n: number): CatalogData {
  const data = emptyCatalog()
  for (let i = 0; i < n; i++) {
    const id = `item-${i}`
    data.items.push({ id, name: i === 1 ? 'Arroz Camil Tipo 1 5kg' : `Produto ${i}`, unit: 'UN', sku: `SKU${i}`, fractional: false })
    data.barcodes.push({ id: `bc-${i}`, code: String(7890000000000 + i), itemId: id })
    data.prices.push({ id: `pr-${i}`, itemId: id, priceCents: 100 + i, tableVersion: 1 })
  }

  data.paymentMethods.push({ id: 'pm-1', name: 'Dinheiro', kind: 'Cash' })
  data.operators.push({ id: 'u1', displayName: 'Diego S.' })
  data.printers.push({ id: 'p1', name: 'Bobina 80 mm', kind: 'Receipt' })
  return data
}

const VERSION = { schemaVersion: 1, cursor: 'c1', storeId: 'loja-1', storeName: 'Loja Centro', offlineWindowDays: 7 }

async function device() {
  const locks = new InMemoryLockManager()
  const indexedDB = new IDBFactory()
  const dbName = 'pdv-teste'
  const open = async () => {
    const lock = await acquireWriterLock(writerLockName(dbName), locks)
    return { lock, store: await DexieLocalStore.open({ dbName, lock, indexedDB, IDBKeyRange, attempts: 1 }) }
  }

  return { open }
}

describe('LocalStore (Dexie)', () => {
  it('grava a foto do catálogo e lê da memória: código de barras, preço, nome sem acento, coleções', async () => {
    const { open } = await device()
    const { store } = await open()
    expect(store.catalogState()).toBeNull()

    await store.replaceCatalog(VERSION, catalog(3000), '2026-11-18T12:00:00.000Z')

    expect(store.findByBarcode('7890000000001')?.name).toBe('Arroz Camil Tipo 1 5kg')
    expect(store.findByBarcode('0000')).toBeNull()
    expect(store.priceOf('item-1')?.priceCents).toBe(101)
    expect(store.searchItems('arro cami').map((i) => i.id)).toEqual(['item-1'])
    expect(store.searchItems('ARRÓZ').map((i) => i.id)).toEqual(['item-1'])
    expect(store.searchItems('produto', 10)).toHaveLength(10)
    expect(store.list('operators')).toEqual([{ id: 'u1', displayName: 'Diego S.' }])
    expect(store.get('printers', 'p1')?.name).toBe('Bobina 80 mm')
    expect(store.catalogState()).toMatchObject({ cursor: 'c1', storeId: 'loja-1', storeName: 'Loja Centro', offlineWindowDays: 7, syncedAt: '2026-11-18T12:00:00.000Z', counts: { items: 3000, barcodes: 3000, operators: 1 } })
  })

  it('o que foi gravado volta ao reabrir (outra página do mesmo aparelho), com a foto em blocos', async () => {
    const { open } = await device()
    const first = await open()
    await first.store.replaceCatalog(VERSION, catalog(3000), '2026-11-18T12:00:00.000Z')
    await first.store.setMeta('register', { storeName: 'Loja Centro', registerNumber: 1 })
    first.store.close()
    first.lock.release()

    const again = await open()
    expect(again.store.catalogState()?.counts.items).toBe(3000)
    expect(again.store.findByBarcode('7890000000002')?.id).toBe('item-2')
    expect(await again.store.getMeta('register')).toEqual({ storeName: 'Loja Centro', registerNumber: 1 })
    expect(CHUNKS).toBeLessThan(3000)
  })

  it('o delta altera, inclui e remove (produto desativado) e avança o cursor junto; vale depois de reabrir', async () => {
    const { open } = await device()
    const first = await open()
    await first.store.replaceCatalog(VERSION, catalog(10), '2026-11-18T12:00:00.000Z')
    await first.store.applyCatalogChanges({ schemaVersion: 1, cursor: 'c2' }, [
      { collection: 'prices', op: 'upsert', record: { id: 'pr-1', itemId: 'item-1', priceCents: 2590, tableVersion: 2 } },
      { collection: 'items', op: 'upsert', record: { id: 'item-new', name: 'Feijão Preto', unit: 'UN', sku: null, fractional: false } },
      { collection: 'barcodes', op: 'upsert', record: { id: 'bc-new', code: '123', itemId: 'item-new' } },
      { collection: 'items', op: 'delete', id: 'item-2' },
      { collection: 'barcodes', op: 'delete', id: 'bc-2' },
    ], '2026-11-18T13:00:00.000Z')

    const check = (s: DexieLocalStore) => {
      expect(s.priceOf('item-1')?.priceCents).toBe(2590)
      expect(s.findByBarcode('123')?.name).toBe('Feijão Preto')
      expect(s.get('items', 'item-2')).toBeNull()
      expect(s.findByBarcode('7890000000002')).toBeNull()
      expect(s.catalogState()).toMatchObject({ cursor: 'c2', syncedAt: '2026-11-18T13:00:00.000Z', offlineWindowDays: 7, counts: { items: 10 } })
    }

    check(first.store)
    first.store.close()
    first.lock.release()
    check((await open()).store)
  })

  it('compacta a sobreposição do delta nos blocos quando passa do limite, sem perder nada', async () => {
    const { open } = await device()
    const first = await open()
    await first.store.replaceCatalog(VERSION, catalog(100), '2026-11-18T12:00:00.000Z')
    const changes = Array.from({ length: OVERLAY_COMPACT_AT }, (_, i) => ({ collection: 'items' as const, op: 'upsert' as const, record: { id: `extra-${i}`, name: `Extra ${i}`, unit: 'UN', sku: null, fractional: false } }))
    await first.store.applyCatalogChanges({ schemaVersion: 1, cursor: 'c3' }, changes, '2026-11-18T14:00:00.000Z')
    first.store.close()
    first.lock.release()

    const again = await open()
    expect(again.store.catalogState()?.counts.items).toBe(100 + OVERLAY_COMPACT_AT)
    expect(again.store.get('items', `extra-${OVERLAY_COMPACT_AT - 1}`)?.name).toBe(`Extra ${OVERLAY_COMPACT_AT - 1}`)
  })

  it('fila de saída: Guid v7 do aparelho, seq contínuo e o resumo com a mais antiga', async () => {
    const { open } = await device()
    const { store } = await open()
    expect(await store.pendingSummary()).toEqual({ count: 0, oldestAt: null })
    const a = await store.enqueue('SaleCompleted', { totalCents: 1990 }, '2026-11-18T10:00:00.000Z')
    const b = await store.enqueue('SaleCompleted', { totalCents: 500 }, '2026-11-18T10:05:00.000Z')
    expect(isUuidV7(a.id)).toBe(true)
    expect(b.seq).toBe(a.seq + 1)
    expect(await store.pendingSummary()).toEqual({ count: 2, oldestAt: '2026-11-18T10:00:00.000Z' })
  })

  it('segunda aba: lê, mas não grava ("O caixa já está aberto em outra janela."); assume quando a primeira fecha', async () => {
    const { open } = await device()
    const first = await open()
    await first.store.replaceCatalog(VERSION, catalog(5), '2026-11-18T12:00:00.000Z')

    const second = await open()
    expect(first.store.writable).toBe(true)
    expect(second.store.writable).toBe(false)
    expect(second.store.findByBarcode('7890000000003')?.id).toBe('item-3')
    await expect(second.store.setMeta('x', 1)).rejects.toBeInstanceOf(ReadOnlyStoreError)
    await expect(second.store.enqueue('SaleCompleted', {})).rejects.toBeInstanceOf(ReadOnlyStoreError)
    await expect(second.store.replaceCatalog(VERSION, catalog(1), '2026-11-18T12:00:00.000Z')).rejects.toBeInstanceOf(ReadOnlyStoreError)

    // A primeira grava mais e fecha: a segunda vira dona e relê o que a primeira gravou.
    await first.store.enqueue('SaleCompleted', { totalCents: 100 }, '2026-11-18T12:30:00.000Z')
    first.store.close()
    first.lock.release()
    await second.lock.whenOwned()
    await second.store.reload()
    expect(second.store.writable).toBe(true)
    expect((await second.store.pendingSummary()).count).toBe(1)
    await second.store.setMeta('x', 1)
    expect(await second.store.getMeta('x')).toBe(1)
  })

  it('erro ao abrir: tenta de novo e propaga sem apagar o banco', async () => {
    const lock = await acquireWriterLock('erro', new InMemoryLockManager())
    const broken = { open: () => { throw new DOMException('Internal error opening backing store', 'UnknownError') } } as unknown as IDBFactory
    await expect(DexieLocalStore.open({ dbName: 'x', lock, indexedDB: broken, IDBKeyRange, attempts: 2, retryDelayMs: 1 })).rejects.toBeTruthy()
  })
})
