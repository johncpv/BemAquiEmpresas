import { CATALOG_SCHEMA_VERSION, type CatalogChange, type CatalogData, type CatalogVersion } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import type { OfflineVerifier } from '@/pdv/offline/offline-crypto'

/**
 * Descida do catálogo da loja para o aparelho (OFF-01 CA 10 e 11), contra a porta `CatalogSyncSource`: a primeira vez baixa a **foto**
 * (`GET /api/v1/pos/sync/snapshot`); depois, só o **delta** desde o cursor (`GET /api/v1/pos/sync/changes?cursor=`). O que chega vai para o
 * `LocalStore` numa transação junto com o cursor novo, então uma queda no meio nunca deixa o cursor à frente dos dados.
 *
 * Quem implementa a porta é o adaptador do cliente Orval (assinatura do aparelho, conversão do decimal para centavos); ele entra depois do merge
 * do backend da OFF-01. Aqui, e nos testes, a porta é uma interface: a tela nunca espera a rede para vender (local-first).
 */
export interface CatalogSnapshot extends CatalogVersion {
  storeId: string
  storeName?: string | null
  data: CatalogData
  /** OFF-03: os verificadores sem rede e o horário do servidor; vão cifrados para o `LocalStore` pelo `onAccess`, nunca para o catálogo. */
  access?: OfflineAccessDownload | null
}

/** OFF-03 CA 1 e 14: o que desce para a entrada sem rede (verificadores, nunca o PIN) e o horário do servidor (piso da janela). */
export interface OfflineAccessDownload {
  verifiers: OfflineVerifier[]
  serverAt: string
}

export interface CatalogChanges extends CatalogVersion {
  access?: OfflineAccessDownload | null
  changes: CatalogChange[]
  /** Há mais mudanças depois deste cursor (o servidor pagina o delta): o cliente pede de novo na hora. */
  hasMore: boolean
  /** O cursor ficou velho demais (ou a lista de coleções mudou): o servidor manda baixar a foto inteira de novo. */
  resetRequired?: boolean
}

export interface CatalogSyncSource {
  /** Foto completa das coleções da loja do aparelho. `onProgress` recebe de 0 a 1 enquanto baixa, se o adaptador souber o tamanho. */
  snapshot(options?: { signal?: AbortSignal; onProgress?: (fraction: number) => void }): Promise<CatalogSnapshot>
  /** Mudanças desde o cursor (inclusive remoções e produto desativado, como `delete`). */
  changes(cursor: string, options?: { signal?: AbortSignal }): Promise<CatalogChanges>
}

export type CatalogSyncResult =
  | { kind: 'snapshot'; items: number; cursor: string }
  | { kind: 'delta'; changes: number; cursor: string }

export class CatalogSchemaError extends Error {
  readonly serverVersion: number
  constructor(serverVersion: number) {
    super(`O servidor mandou o catálogo na versão ${serverVersion}, e este app entende a ${CATALOG_SCHEMA_VERSION}. Atualize o app do caixa.`)
    this.name = 'CatalogSchemaError'
    this.serverVersion = serverVersion
  }
}

/** Etapas para a tela "Preparando o caixa: baixando produtos": download vai até 80%, a gravação fecha os 20% finais. */
export type SyncProgress = { phase: 'download' | 'save'; fraction: number }

const DOWNLOAD_SHARE = 0.8
const MAX_DELTA_PAGES = 50

function assertSchema(version: CatalogVersion): void {
  if (version.schemaVersion !== CATALOG_SCHEMA_VERSION) throw new CatalogSchemaError(version.schemaVersion)
}

/** A janela sem sincronizar vem da loja, com teto de 7 dias (PP-10). Valor fora do intervalo vira o teto; ausente fica ausente. */
export function clampOfflineWindowDays(days: number | null | undefined): number | null {
  if (days === null || days === undefined || !Number.isFinite(days)) return null
  return Math.min(Math.max(Math.floor(days), 1), 7)
}

/**
 * Sincroniza uma vez: sem catálogo local (ou com versão de esquema diferente), baixa a foto; com catálogo, puxa o delta até acabar.
 * O servidor pedindo `resetRequired` cai na foto inteira.
 */
export interface SyncOptions {
  now?: () => Date
  signal?: AbortSignal
  onProgress?: (progress: SyncProgress) => void
  /** OFF-03: recebe os verificadores e o horário do servidor depois que o catálogo foi gravado (cifra e guarda à parte). */
  onAccess?: (access: OfflineAccessDownload) => Promise<void>
}

export async function syncCatalog(store: LocalStore, source: CatalogSyncSource, options: SyncOptions = {}): Promise<CatalogSyncResult> {
  const now = options.now ?? (() => new Date())
  const state = store.catalogState()

  if (state && state.schemaVersion === CATALOG_SCHEMA_VERSION) {
    let cursor = state.cursor
    let total = 0
    for (let page = 0; page < MAX_DELTA_PAGES; page++) {
      const delta = await source.changes(cursor, { signal: options.signal })
      if (delta.resetRequired) return downloadSnapshot(store, source, now, options)
      assertSchema(delta)
      await store.applyCatalogChanges({ ...delta, offlineWindowDays: delta.offlineWindowDays === undefined ? undefined : clampOfflineWindowDays(delta.offlineWindowDays) }, delta.changes, now().toISOString())
      total += delta.changes.length
      if (delta.access) await options.onAccess?.(delta.access)
      cursor = delta.cursor
      if (!delta.hasMore) break
    }

    return { kind: 'delta', changes: total, cursor }
  }

  return downloadSnapshot(store, source, now, options)
}

async function downloadSnapshot(store: LocalStore, source: CatalogSyncSource, now: () => Date, options: SyncOptions): Promise<CatalogSyncResult> {
  options.onProgress?.({ phase: 'download', fraction: 0 })
  const snapshot = await source.snapshot({
    signal: options.signal,
    onProgress: (f) => options.onProgress?.({ phase: 'download', fraction: Math.min(Math.max(f, 0), 1) * DOWNLOAD_SHARE }),
  })
  assertSchema(snapshot)
  options.onProgress?.({ phase: 'save', fraction: DOWNLOAD_SHARE })
  await store.replaceCatalog({ ...snapshot, offlineWindowDays: clampOfflineWindowDays(snapshot.offlineWindowDays) }, snapshot.data, now().toISOString())
  if (snapshot.access) await options.onAccess?.(snapshot.access)
  options.onProgress?.({ phase: 'save', fraction: 1 })
  return { kind: 'snapshot', items: snapshot.data.items.length, cursor: snapshot.cursor }
}
