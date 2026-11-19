import { CATALOG_COLLECTIONS, type CatalogBarcode, type CatalogChange, type CatalogCollection, type CatalogData, type CatalogItem, type CatalogPrice, type CatalogRecords } from './catalog-types'

/** Texto de busca: minúsculo, sem acento, espaços simples. */
export function normalizeSearch(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/\s+/g, ' ').trim()
}

type Maps = { [K in CatalogCollection]: Map<string, CatalogRecords[K]> }

/**
 * O catálogo em memória (SPK-03: 50 mil itens cabem em ~20 MB de heap; código de barras em menos de 0,1 ms e nome em menos de 10 ms no p95).
 * Mapas por id de cada coleção, mais os índices por código de barras e por item do preço. A lista de nomes para a busca por trecho é refeita
 * só quando alguém busca depois de uma mudança.
 */
export class CatalogMemory {
  private maps: Maps = emptyMaps()
  private byCode = new Map<string, CatalogBarcode>()
  private priceByItem = new Map<string, CatalogPrice>()
  private names: { text: string; item: CatalogItem }[] | null = null

  load(data: CatalogData): void {
    this.maps = emptyMaps()
    for (const collection of CATALOG_COLLECTIONS) {
      const map = this.maps[collection] as Map<string, { id: string }>
      for (const record of data[collection]) map.set(record.id, record)
    }

    this.reindex()
  }

  apply(changes: readonly CatalogChange[]): void {
    for (const change of changes) {
      const map = this.maps[change.collection] as Map<string, { id: string }>
      if (change.op === 'upsert') map.set(change.record.id, change.record)
      else map.delete(change.id)
    }

    this.reindex()
  }

  private reindex(): void {
    this.byCode = new Map()
    for (const b of this.maps.barcodes.values()) this.byCode.set(b.code, b)
    this.priceByItem = new Map()
    for (const p of this.maps.prices.values()) {
      const current = this.priceByItem.get(p.itemId)
      if (!current || p.tableVersion >= current.tableVersion) this.priceByItem.set(p.itemId, p)
    }

    this.names = null
  }

  data(): CatalogData {
    return Object.fromEntries(CATALOG_COLLECTIONS.map((c) => [c, [...this.maps[c].values()]])) as unknown as CatalogData
  }

  counts(): Record<CatalogCollection, number> {
    return Object.fromEntries(CATALOG_COLLECTIONS.map((c) => [c, this.maps[c].size])) as Record<CatalogCollection, number>
  }

  list<K extends CatalogCollection>(collection: K): CatalogRecords[K][] {
    return [...this.maps[collection].values()]
  }

  get<K extends CatalogCollection>(collection: K, id: string): CatalogRecords[K] | null {
    return this.maps[collection].get(id) ?? null
  }

  findByBarcode(code: string): CatalogItem | null {
    return this.lookupBarcode(code)?.item ?? null
  }

  /** O item e o código lido, com a quantidade da embalagem (PDV-02 CA 2). */
  lookupBarcode(code: string): { item: CatalogItem; barcode: CatalogBarcode } | null {
    const barcode = this.byCode.get(code.trim())
    const item = barcode ? this.maps.items.get(barcode.itemId) : undefined
    return barcode && item ? { item, barcode } : null
  }

  priceOf(itemId: string): CatalogPrice | null {
    return this.priceByItem.get(itemId) ?? null
  }

  searchItems(term: string, limit = 50): CatalogItem[] {
    const tokens = normalizeSearch(term).split(' ').filter(Boolean)
    if (tokens.length === 0) return []
    this.names ??= [...this.maps.items.values()].map((item) => ({ text: normalizeSearch(`${item.name} ${item.sku ?? ''}`), item }))
    const found: CatalogItem[] = []
    for (const { text, item } of this.names) {
      if (tokens.every((t) => text.includes(t))) {
        found.push(item)
        if (found.length >= limit) break
      }
    }

    return found
  }
}

function emptyMaps(): Maps {
  return { items: new Map(), barcodes: new Map(), prices: new Map(), paymentMethods: new Map(), operators: new Map(), printers: new Map() }
}
