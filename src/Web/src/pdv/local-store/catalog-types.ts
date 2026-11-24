import type { Cents } from '@/pdv/money'

/**
 * Coleções do catálogo que descem para o aparelho de caixa (OFF-01 CA 10 e 11). A lista é **fechada e versionada**: coleção nova ou campo que
 * muda de sentido sobe o `CATALOG_SCHEMA_VERSION` aqui e o `schemaVersion` do servidor. Só o que a tela de venda precisa desce: nunca custo
 * nem margem (CA 12). Todo registro tem `id` (texto, o Guid do servidor) e o dinheiro já chega em **centavos inteiros** (CA 8): quem converte
 * o decimal da API é o adaptador do `CatalogSyncSource`.
 */
export const CATALOG_SCHEMA_VERSION = 1

export const CATALOG_COLLECTIONS = ['items', 'barcodes', 'prices', 'paymentMethods', 'operators', 'printers'] as const
export type CatalogCollection = (typeof CATALOG_COLLECTIONS)[number]

/** Item vendável da loja (produto ou variação da grade). */
export interface CatalogItem {
  id: string
  name: string
  /** Unidade de venda (UN, KG, L...). */
  unit: string
  sku: string | null
  /** Pode vender fração (balança, metro)? */
  fractional: boolean
}

/** Código de barras (EAN, código interno da balança...) de um item. */
export interface CatalogBarcode {
  id: string
  code: string
  itemId: string
  /**
   * Quantidade que o código representa (PDV-02 CA 2: o código da caixa com 12 lança 12). Ausente nos catálogos baixados antes da PDV-02: vale 1,
   * e o próximo delta do item traz o valor.
   */
  packQuantity?: number
}

/** Preço de venda do item na tabela da loja, com a versão da tabela (para a venda registrar qual preço valeu). */
export interface CatalogPrice {
  id: string
  itemId: string
  priceCents: Cents
  tableVersion: number
}

export interface CatalogPaymentMethod {
  id: string
  name: string
  /** Tipo de meio (dinheiro, Pix, cartão...): o texto do servidor, sem tradução aqui. */
  kind: string
  /**
   * PDV-03: dá troco (só o dinheiro), as bandeiras da tabela da forma (vazio: todas) e o teto de parcelas. Ausentes nos catálogos baixados antes
   * da PDV-03: valem o tipo (`Cash` dá troco), todas as bandeiras e 1 parcela, até o próximo delta da forma.
   */
  allowsChange?: boolean
  cardBrands?: string[]
  maxInstallments?: number
}

/** Quem pode entrar neste caixa (primeiro nome e inicial). O PIN nunca desce. */
export interface CatalogOperator {
  id: string
  displayName: string
}

/**
 * Impressora do caixa (IMP-01 CA 3, 6 e 7): conexão, bobina (58 ou 80 mm), colunas (32, 42 ou 48), modelo e se é a padrão. Os campos novos ficam
 * ausentes nos catálogos baixados antes da IMP-01 (a coleção descia vazia): o próximo delta traz a lista inteira.
 */
export interface CatalogPrinter {
  id: string
  name: string
  kind: string
  connection?: string
  paperWidthMm?: number
  columns?: number
  model?: string
  isDefault?: boolean
}

export interface CatalogRecords {
  items: CatalogItem
  barcodes: CatalogBarcode
  prices: CatalogPrice
  paymentMethods: CatalogPaymentMethod
  operators: CatalogOperator
  printers: CatalogPrinter
}

export type CatalogData = { [K in CatalogCollection]: CatalogRecords[K][] }

/** Uma mudança do delta: inclusão ou alteração traz o registro inteiro; remoção (inclusive produto desativado, CA 11) traz só o id. */
export type CatalogChange = {
  [K in CatalogCollection]: { collection: K; op: 'upsert'; record: CatalogRecords[K] } | { collection: K; op: 'delete'; id: string }
}[CatalogCollection]

/** O que acompanha a foto e o delta. */
export interface CatalogVersion {
  schemaVersion: number
  /** Posição opaca do servidor: o próximo delta começa dela. */
  cursor: string
  /**
   * Janela sem sincronizar da loja, em dias (PP-10): parâmetro da loja, padrão e teto de 7. Ausente: o servidor não mandou e a tela não
   * mostra prazo. O bloqueio da venda quando a janela vence é da OFF-03.
   */
  offlineWindowDays?: number | null
  /** Aviso de sangria e teto de dinheiro no caixa da loja (PP-22, CX-01), em centavos. Ausente: o servidor não mandou (mantém o guardado). */
  cashLimits?: CashLimits | null
  /** Empresa e documento fiscal da loja e o cupom pós-venda (IMP-01). Ausente: o servidor não mandou (mantém o guardado). */
  storeSettings?: StoreSettings | null
}

/**
 * Parâmetros da loja para o cupom (IMP-01): nome e CNPJ da empresa no cabeçalho, o modo fiscal, se a loja pode vender (`canSell` falso: sem o
 * aceite do modo "só cupom não fiscal", até a NFC-e, CA 10) e o que fazer com o cupom ao concluir (CA 5).
 */
export interface StoreSettings {
  companyName: string | null
  companyDocument: string | null
  fiscalMode: 'Nfce' | 'NonFiscal'
  canSell: boolean
  receiptPrintMode: 'Ask' | 'Always' | 'Never'
  /** OFF-02: o último item sai sem supervisor (a loja decide; o servidor confere pelo mesmo valor). Ausente = ligado. */
  freeRemoveLast?: boolean
  /** PDV-04 (PP-38): desconto máximo, em %, sem `pos.discount` nem supervisor. Ausente = 0 (todo desconto pede autorização). */
  discountLimitPercent?: number
}

/** Limites da loja para o dinheiro na gaveta (CX-01 CA 7 e 8), em centavos; null = sem limite. */
export interface CashLimits {
  alertCents: number | null
  ceilingCents: number | null
}

export interface CatalogState extends CatalogVersion {
  storeId: string
  storeName: string | null
  /** Instante (ISO) da última sincronização que deu certo. */
  syncedAt: string
  offlineWindowDays: number | null
  cashLimits: CashLimits | null
  storeSettings?: StoreSettings | null
  counts: Record<CatalogCollection, number>
}

export const emptyCatalog = (): CatalogData => ({ items: [], barcodes: [], prices: [], paymentMethods: [], operators: [], printers: [] })
