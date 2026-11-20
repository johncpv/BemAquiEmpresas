import Dexie, { type Table } from 'dexie'
import { uuidv7 } from '@/pdv/uuid-v7'
import { CatalogMemory } from './catalog-memory'
import { CATALOG_COLLECTIONS, type CatalogChange, type CatalogCollection, type CatalogData, type CatalogState, type CatalogVersion, emptyCatalog } from './catalog-types'
import { CHAIN_META, type ChainHead, type LocalStore, type MetaKey, type OutboxDraft, type OutboxEntry, type OutboxSeal, type PendingSummary, ReadOnlyStoreError } from './local-store'
import type { ClosedSale, CustomerDocumentRecord, SaleJournalEvent } from './sale-journal-types'
import type { WriterLock } from './writer-lock'

/** Banco local do caixa (um por origem, ou seja, por empresa). A chave do aparelho fica à parte, em `bemaqui-pdv` (PDV-01). */
export const PDV_DB_NAME = 'bemaqui-pdv-dados'

/** Blocos por coleção: ~800 itens por bloco com 50 mil itens (o SPK-03 mediu blocos de ~1.000). */
export const CHUNKS = 64
/** Mudanças guardadas à parte antes de regravar os blocos (o delta não regrava o catálogo inteiro; risco do SPK-03). */
export const OVERLAY_COMPACT_AT = 2_000

type MetaRow = { k: string; v: unknown }
type ChunkRow = { k: string; collection: CatalogCollection; records: { id: string }[] }
type OverlayRow = { k: string; collection: CatalogCollection; id: string; record: { id: string } | null }
type OutboxRow = { seq?: number; id: string; kind: string; createdAt: string; payload: unknown; seal?: OutboxSeal }
type CatalogMeta = Omit<CatalogState, 'counts'>
type SaleEventRow = SaleJournalEvent & { seq?: number }

/** Último número de venda usado por este caixa (o próximo é este + 1). */
export const SALE_SEQ_META = 'sale.seq'

export function chunkOf(id: string): number {
  let h = 0
  for (let i = 0; i < id.length; i++) h = (Math.imul(h, 31) + id.charCodeAt(i)) | 0
  return Math.abs(h) % CHUNKS
}

class PdvDatabase extends Dexie {
  meta!: Table<MetaRow, string>
  catalogChunks!: Table<ChunkRow, string>
  catalogOverlay!: Table<OverlayRow, string>
  outbox!: Table<OutboxRow, number>
  saleEvents!: Table<SaleEventRow, number>
  customerDocs!: Table<CustomerDocumentRecord, string>

  constructor(name: string, deps?: { indexedDB: IDBFactory; IDBKeyRange: typeof IDBKeyRange }) {
    // 'strict': cada transação concluída já está no disco (fsync) antes de a promessa resolver; o caixa não perde venda concluída numa queda.
    super(name, { chromeTransactionDurability: 'strict', ...(deps ?? {}) })
    this.version(1).stores({ meta: 'k', catalogChunks: 'k', catalogOverlay: 'k', outbox: '++seq' })
    // PDV-02: diário da venda em andamento e documento do consumidor cifrado (só acréscimo; nada da versão 1 muda).
    this.version(2).stores({ meta: 'k', catalogChunks: 'k', catalogOverlay: 'k', outbox: '++seq', saleEvents: '++seq, saleId', customerDocs: 'id' })
  }
}

export interface OpenOptions {
  lock: WriterLock
  dbName?: string
  /** Para os testes (fake-indexeddb); no navegador, o IndexedDB dele. */
  indexedDB?: IDBFactory
  IDBKeyRange?: typeof IDBKeyRange
  /** Tentativas de abrir (SPK-03: logo depois de o navegador morrer, a 1ª abertura pode falhar e a 2ª dá certo). */
  attempts?: number
  retryDelayMs?: number
}


/** `LocalStore` sobre IndexedDB com Dexie (SPK-03): catálogo em blocos + sobreposição do delta, carregado em memória ao abrir. */
export class DexieLocalStore implements LocalStore {
  private readonly memory = new CatalogMemory()
  private meta: CatalogMeta | null = null
  private overlaySize = 0

  private readonly db: PdvDatabase
  private readonly lock: WriterLock

  private constructor(db: PdvDatabase, lock: WriterLock) {
    this.db = db
    this.lock = lock
  }

  /**
   * Abre o banco e carrega o catálogo na memória. Erro ao abrir: tenta de novo (SPK-03, achado 1) e, se insistir, propaga. **Nunca** apaga o
   * banco por causa de um erro: nele pode haver venda que ainda não subiu.
   */
  static async open(options: OpenOptions): Promise<DexieLocalStore> {
    const deps = options.indexedDB && options.IDBKeyRange ? { indexedDB: options.indexedDB, IDBKeyRange: options.IDBKeyRange } : undefined
    const attempts = options.attempts ?? 3
    let lastError: unknown
    for (let attempt = 1; attempt <= attempts; attempt++) {
      const db = new PdvDatabase(options.dbName ?? PDV_DB_NAME, deps)
      try {
        await db.open()
        const store = new DexieLocalStore(db, options.lock)
        await store.reload()
        return store
      } catch (error) {
        lastError = error
        db.close()
        if (attempt < attempts) await new Promise((resolve) => setTimeout(resolve, options.retryDelayMs ?? 1_000))
      }
    }

    throw lastError
  }

  get writable(): boolean {
    return this.lock.owned
  }

  private assertWritable(): void {
    if (!this.lock.owned) throw new ReadOnlyStoreError()
  }

  async reload(): Promise<void> {
    const [chunks, overlay, meta] = await this.db.transaction('r', this.db.catalogChunks, this.db.catalogOverlay, this.db.meta, async () =>
      [await this.db.catalogChunks.toArray(), await this.db.catalogOverlay.toArray(), await this.db.meta.get('catalog')] as const)
    const data = emptyCatalog()
    for (const chunk of chunks) (data[chunk.collection] as { id: string }[]).push(...chunk.records)
    this.memory.load(data)
    this.memory.apply(overlay.map(toChange))
    this.overlaySize = overlay.length
    this.meta = (meta?.v as CatalogMeta | undefined) ?? null
  }

  async getMeta<T>(key: MetaKey): Promise<T | undefined> {
    return (await this.db.meta.get(key))?.v as T | undefined
  }

  async setMeta(key: MetaKey, value: unknown): Promise<void> {
    this.assertWritable()
    await this.db.meta.put({ k: key, v: value })
  }

  async updateMeta(key: MetaKey, update: (current: unknown) => unknown, entry?: OutboxDraft | ((next: unknown, previous: unknown) => OutboxDraft | null)): Promise<OutboxEntry | null> {
    this.assertWritable()
    return this.db.transaction('rw', this.db.meta, this.db.outbox, async () => {
      const previous = (await this.db.meta.get(key))?.v
      const next = update(previous)
      if (next === null || next === undefined) await this.db.meta.delete(key)
      else await this.db.meta.put({ k: key, v: next })
      const draft = typeof entry === 'function' ? entry(next, previous) : entry ?? null
      if (!draft) return null
      const row: OutboxRow = { id: uuidv7(Date.parse(draft.at)), kind: draft.kind, createdAt: draft.at, payload: draft.payload }
      const seq = await this.db.outbox.add(row)
      return { ...row, seq }
    })
  }

  catalogState(): CatalogState | null {
    return this.meta ? { ...this.meta, cashLimits: this.meta.cashLimits ?? null, storeSettings: this.meta.storeSettings ?? null, counts: this.memory.counts() } : null
  }

  async replaceCatalog(version: CatalogVersion & { storeId: string; storeName?: string | null }, data: CatalogData, syncedAt: string): Promise<void> {
    this.assertWritable()
    const rows: ChunkRow[] = []
    for (const collection of CATALOG_COLLECTIONS) {
      const groups = new Map<number, { id: string }[]>()
      for (const record of data[collection] as { id: string }[]) {
        const k = chunkOf(record.id)
        const group = groups.get(k)
        if (group) group.push(record)
        else groups.set(k, [record])
      }

      for (const [k, records] of groups) rows.push({ k: `${collection}:${k}`, collection, records })
    }

    const meta: CatalogMeta = {
      schemaVersion: version.schemaVersion, cursor: version.cursor, storeId: version.storeId, storeName: version.storeName ?? null,
      offlineWindowDays: version.offlineWindowDays ?? null, cashLimits: version.cashLimits ?? null, storeSettings: version.storeSettings ?? null, syncedAt,
    }
    await this.db.transaction('rw', this.db.catalogChunks, this.db.catalogOverlay, this.db.meta, async () => {
      await this.db.catalogChunks.clear()
      await this.db.catalogOverlay.clear()
      await this.db.catalogChunks.bulkPut(rows)
      await this.db.meta.put({ k: 'catalog', v: meta })
    })
    this.memory.load(data)
    this.overlaySize = 0
    this.meta = meta
  }

  async applyCatalogChanges(version: CatalogVersion, changes: readonly CatalogChange[], syncedAt: string): Promise<void> {
    this.assertWritable()
    if (!this.meta) throw new Error('Não há catálogo para aplicar o delta: baixe a foto primeiro.')
    const meta: CatalogMeta = {
      ...this.meta, schemaVersion: version.schemaVersion, cursor: version.cursor, syncedAt,
      offlineWindowDays: version.offlineWindowDays === undefined ? this.meta.offlineWindowDays : version.offlineWindowDays,
      cashLimits: version.cashLimits === undefined ? this.meta.cashLimits ?? null : version.cashLimits,
      storeSettings: version.storeSettings === undefined ? this.meta.storeSettings ?? null : version.storeSettings,
    }
    const rows: OverlayRow[] = changes.map((c) => c.op === 'upsert'
      ? { k: `${c.collection}:${c.record.id}`, collection: c.collection, id: c.record.id, record: c.record }
      : { k: `${c.collection}:${c.id}`, collection: c.collection, id: c.id, record: null })
    await this.db.transaction('rw', this.db.catalogOverlay, this.db.meta, async () => {
      if (rows.length) await this.db.catalogOverlay.bulkPut(rows)
      await this.db.meta.put({ k: 'catalog', v: meta })
    })
    this.memory.apply(changes)
    this.meta = meta
    this.overlaySize = await this.db.catalogOverlay.count()
    if (this.overlaySize >= OVERLAY_COMPACT_AT) await this.compact()
  }

  /** Regrava os blocos tocados pela sobreposição e limpa a sobreposição, numa transação. */
  async compact(): Promise<void> {
    this.assertWritable()
    const data = this.memory.data()
    await this.db.transaction('rw', this.db.catalogChunks, this.db.catalogOverlay, async () => {
      const overlay = await this.db.catalogOverlay.toArray()
      const touched = new Set(overlay.map((o) => `${o.collection}:${chunkOf(o.id)}`))
      const rows: ChunkRow[] = []
      for (const key of touched) {
        const [collection, n] = key.split(':') as [CatalogCollection, string]
        const records = (data[collection] as { id: string }[]).filter((r) => chunkOf(r.id) === Number(n))
        rows.push({ k: key, collection, records })
      }

      await this.db.catalogChunks.bulkPut(rows)
      await this.db.catalogOverlay.clear()
    })
    this.overlaySize = 0
  }

  list<K extends CatalogCollection>(collection: K) { return this.memory.list(collection) }
  get<K extends CatalogCollection>(collection: K, id: string) { return this.memory.get(collection, id) }
  findByBarcode(code: string) { return this.memory.findByBarcode(code) }
  lookupBarcode(code: string) { return this.memory.lookupBarcode(code) }
  priceOf(itemId: string) { return this.memory.priceOf(itemId) }
  searchItems(term: string, limit?: number) { return this.memory.searchItems(term, limit) }

  async enqueue(kind: string, payload: unknown, at: string = new Date().toISOString()): Promise<OutboxEntry> {
    this.assertWritable()
    const row: OutboxRow = { id: uuidv7(Date.parse(at)), kind, createdAt: at, payload }
    const seq = await this.db.outbox.add(row)
    return { ...row, seq }
  }

  async outbox(limit = 500): Promise<OutboxEntry[]> {
    const rows = await this.db.outbox.orderBy('seq').limit(limit).toArray()
    return rows.map((r) => ({ ...r, seq: r.seq! }))
  }

  async sealOutbox(seals: readonly { seq: number; seal: OutboxSeal }[], expected: ChainHead | null, next: ChainHead): Promise<boolean> {
    this.assertWritable()
    return this.db.transaction('rw', this.db.meta, this.db.outbox, async () => {
      const current = ((await this.db.meta.get(CHAIN_META))?.v as ChainHead | undefined) ?? null
      if (JSON.stringify(current) !== JSON.stringify(expected)) return false
      for (const { seq, seal } of seals) {
        const row = await this.db.outbox.get(seq)
        if (!row || row.seal) return false
        await this.db.outbox.put({ ...row, seal })
      }

      await this.db.meta.put({ k: CHAIN_META, v: next })
      return true
    })
  }

  async removeOutbox(seqs: readonly number[]): Promise<void> {
    this.assertWritable()
    if (seqs.length === 0) return
    await this.db.outbox.bulkDelete([...seqs])
  }

  async pendingSummary(): Promise<PendingSummary> {
    const [count, oldest] = await this.db.transaction('r', this.db.outbox, async () => [await this.db.outbox.count(), await this.db.outbox.orderBy('seq').first()] as const)
    return { count, oldestAt: oldest?.createdAt ?? null }
  }

  async appendSaleEvents(events: readonly SaleJournalEvent[]): Promise<void> {
    this.assertWritable()
    if (events.length === 0) return
    await this.db.transaction('rw', this.db.saleEvents, async () => { await this.db.saleEvents.bulkAdd(events.map((e) => ({ ...e }))) })
  }

  async saleJournal(): Promise<SaleJournalEvent[]> {
    const rows = await this.db.saleEvents.orderBy('seq').toArray()
    return rows.map(({ seq: _seq, ...event }) => event)
  }

  async closeSale(saleId: string, build: (number: number, events: SaleJournalEvent[]) => ClosedSale, deleteDocumentId?: string | null): Promise<{ number: number; entry: OutboxEntry }> {
    this.assertWritable()
    return this.db.transaction('rw', [this.db.meta, this.db.saleEvents, this.db.outbox, this.db.customerDocs], async () => {
      const last = Number((await this.db.meta.get(SALE_SEQ_META))?.v ?? 0)
      const number = last + 1
      const rows = await this.db.saleEvents.where('saleId').equals(saleId).sortBy('seq')
      const closed = build(number, rows.map(({ seq: _seq, ...event }) => event))
      const row: OutboxRow = { id: uuidv7(Date.parse(closed.at)), kind: closed.kind, createdAt: closed.at, payload: closed.payload }
      const seq = await this.db.outbox.add(row)
      await this.db.saleEvents.where('saleId').equals(saleId).delete()
      await this.db.meta.put({ k: SALE_SEQ_META, v: number })
      if (deleteDocumentId) await this.db.customerDocs.delete(deleteDocumentId)
      return { number, entry: { ...row, seq } }
    })
  }

  async putCustomerDocument(doc: CustomerDocumentRecord): Promise<void> {
    this.assertWritable()
    await this.db.customerDocs.put(doc)
  }

  async getCustomerDocument(id: string): Promise<CustomerDocumentRecord | null> {
    return (await this.db.customerDocs.get(id)) ?? null
  }

  async deleteCustomerDocument(id: string): Promise<void> {
    this.assertWritable()
    await this.db.customerDocs.delete(id)
  }

  close(): void {
    this.db.close()
  }
}

function toChange(row: OverlayRow): CatalogChange {
  return (row.record ? { collection: row.collection, op: 'upsert', record: row.record } : { collection: row.collection, op: 'delete', id: row.id }) as CatalogChange
}
