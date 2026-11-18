import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { CatalogSchemaError, type CatalogChanges, type CatalogSnapshot, type CatalogSyncSource, clampOfflineWindowDays, type SyncProgress, syncCatalog } from './catalog-sync'
import { type CatalogChange, type CatalogData, emptyCatalog } from './local-store/catalog-types'
import { DexieLocalStore } from './local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager } from './local-store/writer-lock'

/**
 * OFF-01 CA 10 e 11 do lado do aparelho, contra uma fonte falsa (o endpoint real e o cliente Orval entram depois do merge do backend): a primeira
 * sincronização baixa a foto, as seguintes só o delta desde o cursor, e tudo vai para o LocalStore.
 */
class FakeSource implements CatalogSyncSource {
  snapshots = 0
  cursors: string[] = []
  pages: CatalogChanges[] = []
  constructor(public snap: CatalogSnapshot) {}

  async snapshot(options?: { onProgress?: (fraction: number) => void }) {
    this.snapshots++
    options?.onProgress?.(0.5)
    options?.onProgress?.(1)
    return structuredClone(this.snap)
  }

  async changes(cursor: string) {
    this.cursors.push(cursor)
    return this.pages.shift() ?? { schemaVersion: 1, cursor, changes: [], hasMore: false }
  }
}

function data(): CatalogData {
  const d = emptyCatalog()
  d.items.push({ id: 'i1', name: 'Café 500 g', unit: 'UN', sku: null, fractional: false }, { id: 'i2', name: 'Açúcar 1 kg', unit: 'UN', sku: null, fractional: false })
  d.barcodes.push({ id: 'b1', code: '789100', itemId: 'i1' }, { id: 'b2', code: '789200', itemId: 'i2' })
  d.prices.push({ id: 'p1', itemId: 'i1', priceCents: 1890, tableVersion: 4 }, { id: 'p2', itemId: 'i2', priceCents: 549, tableVersion: 4 })
  d.paymentMethods.push({ id: 'm1', name: 'Pix', kind: 'Pix' })
  d.operators.push({ id: 'u1', displayName: 'Ana S.' })
  return d
}

const snapshot = (over: Partial<CatalogSnapshot> = {}): CatalogSnapshot => ({ schemaVersion: 1, cursor: 'c10', storeId: 's1', storeName: 'Loja Centro', offlineWindowDays: 5, data: data(), ...over })

async function openStore() {
  const lock = await acquireWriterLock('sync', new InMemoryLockManager())
  return DexieLocalStore.open({ dbName: `sync-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
}

const clock = (iso: string) => () => new Date(iso)

describe('descida do catálogo (CatalogSyncSource falso)', () => {
  it('aparelho sem catálogo: baixa a foto da loja com progresso e grava no LocalStore', async () => {
    const store = await openStore()
    const source = new FakeSource(snapshot())
    const progress: SyncProgress[] = []

    const result = await syncCatalog(store, source, { now: clock('2026-11-18T12:00:00Z'), onProgress: (p) => progress.push(p) })

    expect(result).toEqual({ kind: 'snapshot', items: 2, cursor: 'c10' })
    expect(source.snapshots).toBe(1)
    expect(store.findByBarcode('789100')?.name).toBe('Café 500 g')
    expect(store.priceOf('i2')?.priceCents).toBe(549)
    expect(store.list('paymentMethods')).toHaveLength(1)
    expect(store.catalogState()).toMatchObject({ cursor: 'c10', storeId: 's1', offlineWindowDays: 5, syncedAt: '2026-11-18T12:00:00.000Z' })
    expect(progress.map((p) => p.fraction)).toEqual([0, 0.4, 0.8, 0.8, 1])
  })

  it('aparelho com foto: só o delta desde o cursor, em páginas, com produto desativado chegando como removido', async () => {
    const store = await openStore()
    const source = new FakeSource(snapshot())
    await syncCatalog(store, source, { now: clock('2026-11-18T12:00:00Z') })
    const page1: CatalogChange[] = [{ collection: 'prices', op: 'upsert', record: { id: 'p1', itemId: 'i1', priceCents: 1990, tableVersion: 5 } }]
    const page2: CatalogChange[] = [{ collection: 'items', op: 'delete', id: 'i2' }, { collection: 'barcodes', op: 'delete', id: 'b2' }]
    source.pages = [{ schemaVersion: 1, cursor: 'c11', changes: page1, hasMore: true }, { schemaVersion: 1, cursor: 'c12', changes: page2, hasMore: false }]

    const result = await syncCatalog(store, source, { now: clock('2026-11-18T12:05:00Z') })

    expect(result).toEqual({ kind: 'delta', changes: 3, cursor: 'c12' })
    expect(source.snapshots).toBe(1)
    expect(source.cursors).toEqual(['c10', 'c11'])
    expect(store.priceOf('i1')?.priceCents).toBe(1990)
    expect(store.findByBarcode('789200')).toBeNull()
    expect(store.get('items', 'i2')).toBeNull()
    expect(store.catalogState()).toMatchObject({ cursor: 'c12', syncedAt: '2026-11-18T12:05:00.000Z', counts: { items: 1 } })
  })

  it('delta vazio também conta como sincronização (atualiza a hora da faixa)', async () => {
    const store = await openStore()
    const source = new FakeSource(snapshot())
    await syncCatalog(store, source, { now: clock('2026-11-18T12:00:00Z') })
    await syncCatalog(store, source, { now: clock('2026-11-18T12:30:00Z') })
    expect(store.catalogState()?.syncedAt).toBe('2026-11-18T12:30:00.000Z')
  })

  it('servidor pede foto nova (cursor velho): baixa a foto inteira de novo', async () => {
    const store = await openStore()
    const source = new FakeSource(snapshot())
    await syncCatalog(store, source)
    source.snap = snapshot({ cursor: 'c99', data: { ...emptyCatalog(), items: [{ id: 'i9', name: 'Leite', unit: 'L', sku: null, fractional: false }] } })
    source.pages = [{ schemaVersion: 1, cursor: 'c10', changes: [], hasMore: false, resetRequired: true }]

    expect(await syncCatalog(store, source)).toEqual({ kind: 'snapshot', items: 1, cursor: 'c99' })
    expect(store.get('items', 'i1')).toBeNull()
    expect(store.get('items', 'i9')?.name).toBe('Leite')
  })

  it('versão do catálogo que o app não entende: erro claro e nada gravado', async () => {
    const store = await openStore()
    await expect(syncCatalog(store, new FakeSource(snapshot({ schemaVersion: 2 })))).rejects.toBeInstanceOf(CatalogSchemaError)
    expect(store.catalogState()).toBeNull()
  })

  it('falha no meio do delta não avança o cursor além do que foi gravado', async () => {
    const store = await openStore()
    const source = new FakeSource(snapshot())
    await syncCatalog(store, source)
    source.pages = [{ schemaVersion: 1, cursor: 'c11', changes: [], hasMore: true }]
    source.changes = async (cursor: string) => {
      source.cursors.push(cursor)
      if (cursor === 'c11') throw new Error('rede caiu')
      return source.pages.shift()!
    }

    await expect(syncCatalog(store, source)).rejects.toThrow('rede caiu')
    expect(store.catalogState()?.cursor).toBe('c11')
  })

  it('PP-10: janela sem sincronizar com teto de 7 dias; ausente fica ausente', () => {
    expect(clampOfflineWindowDays(30)).toBe(7)
    expect(clampOfflineWindowDays(3)).toBe(3)
    expect(clampOfflineWindowDays(0)).toBe(1)
    expect(clampOfflineWindowDays(null)).toBeNull()
    expect(clampOfflineWindowDays(undefined)).toBeNull()
  })
})
