import type { CatalogBarcode, CatalogChange, CatalogCollection, CatalogData, CatalogItem, CatalogPrice, CatalogRecords, CatalogState, CatalogVersion } from './catalog-types'
import type { ClosedSale, CustomerDocumentRecord, SaleJournalEvent } from './sale-journal-types'

/**
 * Porta `LocalStore` (OFF-01 CA 7, rascunho A item 4): todo dado local do caixa passa por aqui. A implementação é IndexedDB com Dexie (escolha
 * do SPK-03), com o catálogo em blocos e indexado em memória; quem usa a porta não sabe disso, e trocar de armazenamento (o SQLite-WASM volta a
 * ser candidato se o catálogo não couber na memória) não muda tela nenhuma. Nenhum código fora de `src/pdv/local-store/` usa IndexedDB ou Dexie
 * direto: a regra `scripts/no-raw-indexeddb.mjs` do lint barra.
 *
 * Só a aba dona do Web Lock escreve (CA 6): as gravações de uma aba sem o lock falham com `ReadOnlyStoreError`; a leitura vale nas duas.
 */
export interface LocalStore {
  /** Esta aba pode gravar (é a dona do lock)? */
  readonly writable: boolean

  /** Valor solto do aparelho (último aperto de mão, aviso de armazenamento, etc.). */
  getMeta<T>(key: MetaKey): Promise<T | undefined>
  setMeta(key: MetaKey, value: unknown): Promise<void>
  /**
   * Lê, troca e grava um valor do aparelho e, junto, põe (ou não) um evento na fila de saída, **numa transação só** (CX-01: a sessão de caixa e o
   * evento da abertura, da sangria ou do suprimento nunca ficam um sem o outro). `update` recebe o valor atual e devolve o novo (`null` apaga);
   * se lançar erro, nada é gravado. `entry` é o evento fixo ou uma função do valor novo e do anterior (null = sem evento).
   */
  updateMeta(key: MetaKey, update: (current: unknown) => unknown, entry?: OutboxDraft | ((next: unknown, previous: unknown) => OutboxDraft | null)): Promise<OutboxEntry | null>

  /** Estado do catálogo guardado (null: o aparelho ainda não baixou a foto). Vem da memória, sem ir ao disco. */
  catalogState(): CatalogState | null
  /** Troca o catálogo inteiro pela foto (primeira sincronização, ou o servidor pediu foto nova), numa transação só. */
  replaceCatalog(version: CatalogVersion & { storeId: string; storeName?: string | null }, data: CatalogData, syncedAt: string): Promise<void>
  /** Aplica o delta e avança o cursor juntos (delta vazio também conta como sincronização: atualiza a hora). */
  applyCatalogChanges(version: CatalogVersion, changes: readonly CatalogChange[], syncedAt: string): Promise<void>

  list<K extends CatalogCollection>(collection: K): readonly CatalogRecords[K][]
  get<K extends CatalogCollection>(collection: K, id: string): CatalogRecords[K] | null
  findByBarcode(code: string): CatalogItem | null
  /** O item e o código lido, com a quantidade da embalagem (PDV-02 CA 2). */
  lookupBarcode(code: string): { item: CatalogItem; barcode: CatalogBarcode } | null
  priceOf(itemId: string): CatalogPrice | null
  /** Busca pelo nome, por pedaços de palavra, sem acento e sem diferença de maiúscula ("arro cami" acha "Arroz Camil"). */
  searchItems(term: string, limit?: number): CatalogItem[]

  /** Põe um evento na fila de saída (a subida e o protocolo são da OFF-02). */
  enqueue(kind: string, payload: unknown, at?: string): Promise<OutboxEntry>
  /** Quantas vendas esperam para subir e desde quando (faixa de estado, CA 13). */
  pendingSummary(): Promise<PendingSummary>

  /** Grava eventos da venda em andamento no diário (PDV-02), numa transação; a tela só confirma depois disso. */
  appendSaleEvents(events: readonly SaleJournalEvent[]): Promise<void>
  /** O diário inteiro, na ordem em que foi gravado (vendas abertas e em espera). */
  saleJournal(): Promise<SaleJournalEvent[]>
  /**
   * Fecha a venda numa transação só: atribui o próximo número do caixa, tira os eventos dela do diário e põe uma entrada na fila de saída,
   * montada por `build` com o número e os eventos. `deleteDocumentId`: o documento do consumidor que deixa de ser preciso (venda descartada).
   */
  closeSale(saleId: string, build: (number: number, events: SaleJournalEvent[]) => ClosedSale, deleteDocumentId?: string | null): Promise<{ number: number; entry: OutboxEntry }>
  putCustomerDocument(doc: CustomerDocumentRecord): Promise<void>
  getCustomerDocument(id: string): Promise<CustomerDocumentRecord | null>
  deleteCustomerDocument(id: string): Promise<void>

  /** Relê do disco (a segunda aba, quando vira dona, pega o que a primeira gravou). */
  reload(): Promise<void>
  close(): void
}

export type MetaKey =
  | 'register'           // último aperto de mão que deu certo: caixa e loja, para abrir sem rede (CA 2)
  | 'storage.persist'    // resultado do navigator.storage.persist() (CA 5), para subir na sincronização
  | 'catalog'            // versão e cursor do catálogo (uso interno do LocalStore)
  | (string & {})

export interface OutboxEntry {
  /** Ordem local, contínua (o protocolo da OFF-02 confirma pelo ack_seq contíguo). */
  seq: number
  /** Guid v7 gerado no aparelho. */
  id: string
  kind: string
  createdAt: string
  payload: unknown
}

/** Evento para a fila de saída, antes de receber o `seq` e o id. */
export interface OutboxDraft {
  kind: string
  payload: unknown
  at: string
}

export interface PendingSummary {
  count: number
  /** Instante (ISO) do evento mais antigo da fila, ou null com a fila vazia. */
  oldestAt: string | null
}

/** Gravação numa aba que não é a dona do lock (a outra janela do caixa é quem escreve). */
export class ReadOnlyStoreError extends Error {
  constructor() {
    super('O caixa já está aberto em outra janela; esta janela só lê.')
    this.name = 'ReadOnlyStoreError'
  }
}
