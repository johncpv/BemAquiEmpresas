import { type Cents, lineTotalCents, sumCents } from '@/pdv/money'
import type { SaleJournalEvent } from '@/pdv/local-store/sale-journal-types'
import type { Tender } from '@/pdv/payment/payment-model'
import { subtractQuantities } from './quantity'

/**
 * A venda em andamento no caixa (PDV-02), refeita a partir do diário de eventos do aparelho (`LocalStore.saleJournal`). Função pura: a mesma
 * lista de eventos dá sempre a mesma venda, e é assim que o caixa volta como estava depois de uma queda de energia (CA 18).
 *
 * Eventos (o nome é o da story; o conteúdo, o `data` de cada um):
 * - `SaleStarted`: a venda começou (no primeiro item);
 * - `LineAdded`: linha nova, com o preço da tabela da loja e a versão da tabela;
 * - `LineChanged`: quantidade ou observação mudou (inclusive o mesmo item lançado de novo, CA 5, e o desfazer do último, CA 10);
 * - `LineRemoved`: linha saiu (o último item, sem supervisor; outra linha, com `pos.cancel_item`);
 * - `SaleParked` e `SaleResumed`: venda em espera e de volta (CA 12);
 * - `SaleDiscarded`: venda descartada com o motivo (CA 13); fecha a venda;
 * - `CustomerSet`: cliente cadastrado e, à parte, o id do registro cifrado do CPF/CNPJ (CA 14, PP-26). O documento nunca entra no evento.
 *
 * Pagamento (PDV-03):
 * - `PaymentStarted`: a venda foi para a tela de pagamento (`InPayment`), com o total daquele instante;
 * - `PaymentCancelled`: "Voltar aos itens" sem nada capturado (a venda volta a `Open`);
 * - `TenderCaptured`: um pagamento capturado (fato imutável: só sai por estorno, PDV-04);
 * - `TenderChanged`: um pagamento em outro estado (`Pending`, `Failed`, `Unknown`), das integrações que chegam com a PAG-02 a PAG-04;
 * - `SaleCompleted`: a soma dos capturados fechou o total; fecha a venda (sobe com o número, pela fila de saída).
 *
 * Desconto, preço e supervisor (PDV-04):
 * - `DiscountApplied`: desconto em centavos na linha (`lineId`) ou no total (sem `lineId`), com o motivo; zero tira o desconto. Acima do limite da
 *   loja vai com quem autorizou (`authorizedBy`) e, se foi o supervisor, a aprovação (`approvalId`);
 * - `PriceOverridden`: o preço praticado da linha (a linha guarda o da tabela), sempre com quem autorizou e o motivo.
 * Mudar os itens tira o desconto no total; mudar a quantidade ou o preço de uma linha tira o desconto dela (o servidor faz o mesmo).
 *
 * Todo evento com dinheiro leva o `operatorId` (no pagamento, o `confirmedBy`).
 */
export type SaleEventType = 'SaleStarted' | 'LineAdded' | 'LineChanged' | 'LineRemoved' | 'SaleParked' | 'SaleResumed' | 'SaleDiscarded' | 'CustomerSet'
  | 'DiscountApplied' | 'PriceOverridden'
  | 'PaymentStarted' | 'PaymentCancelled' | 'TenderCaptured' | 'TenderChanged' | 'SaleCompleted'

export interface SaleLine {
  lineId: string
  itemId: string
  name: string
  unit: string
  fractional: boolean
  /** Texto decimal com ponto (ver quantity.ts). */
  quantity: string
  unitCents: Cents
  /** Versão da tabela de preço que valeu (o servidor confere na subida). */
  priceTableVersion: number
  /** Código lido, quando o item entrou pelo leitor. */
  code: string | null
  note: string | null
  /** PDV-04: preço da tabela quando o praticado (`unitCents`) foi trocado. */
  listUnitCents?: Cents | null
  /** PDV-04: desconto da linha, em centavos, e o motivo. */
  discountCents?: Cents
  discountReason?: string | null
  /** PDV-04: motivo da troca de preço. */
  priceReason?: string | null
}

/** Cliente da venda: o cadastrado (com rede) e/ou o documento avulso, que fica cifrado noutro registro (só o id dele aqui). */
export interface SaleCustomer {
  customerId: string | null
  customerName: string | null
  documentId: string | null
  documentKind: 'cpf' | 'cnpj' | null
}

export interface Sale {
  id: string
  startedAt: string
  status: 'open' | 'parked'
  parkedAt: string | null
  lines: SaleLine[]
  /** O último lançamento (linha e quanto entrou), para o "tirar o último item" sem supervisor (CA 10). */
  lastAdd: { lineId: string; quantity: string } | null
  customer: SaleCustomer | null
  /** Na tela de pagamento (PDV-03): `InPayment` ou, com algo capturado, `PartiallyPaid`. A venda não muda de itens enquanto isso. */
  inPayment: boolean
  /** Pagamentos da venda, na ordem; só os `Captured` abatem do total. */
  tenders: Tender[]
  /** PDV-04: desconto no total, em centavos (o servidor rateia nas linhas), e o motivo. */
  discountCents?: Cents
  discountReason?: string | null
}

/** Quem autorizou a ação que pede permissão (PDV-04): o próprio operador com a permissão, ou o supervisor com a aprovação por PIN. */
export interface SaleAuthorization {
  authorizedBy: string
  approvalId?: string | null
}

export interface SaleBook {
  /** A venda na tela (null: nenhuma começou). */
  currentId: string | null
  /** Vendas abertas e em espera, na ordem em que começaram. */
  sales: Sale[]
}

export type LineChangeReason = 'quantity' | 'note' | 'same-item' | 'remove-last'
export type LineRemoveReason = 'last-item' | 'authorized'

export interface SaleEventData {
  SaleStarted: { operatorId: string | null }
  LineAdded: { line: SaleLine }
  /** `authorizedBy`: quem autorizou a redução que não é o último lançamento (OFF-02: o servidor exige e confere a permissão no instante). */
  LineChanged: { lineId: string; quantity?: string; note?: string | null; reason: LineChangeReason; operatorId: string | null; authorizedBy?: string | null; approvalId?: string | null }
  LineRemoved: { lineId: string; reason: LineRemoveReason; operatorId: string | null; authorizedBy?: string | null; approvalId?: string | null; motive?: string | null }
  DiscountApplied: { lineId: string | null; discountCents: Cents; reason: string | null; operatorId: string | null; authorizedBy?: string | null; approvalId?: string | null }
  PriceOverridden: { lineId: string; unitCents: Cents; reason: string; operatorId: string | null; authorizedBy: string; approvalId?: string | null }
  SaleParked: Record<string, never>
  SaleResumed: Record<string, never>
  SaleDiscarded: { reason: string; operatorId: string | null }
  CustomerSet: { customer: SaleCustomer | null }
  PaymentStarted: { operatorId: string | null; totalCents: Cents }
  PaymentCancelled: { operatorId: string | null }
  TenderCaptured: { tender: Tender }
  TenderChanged: { tender: Tender }
  /** `operatorId`: quem fechou (na conclusão ao reabrir, quem confirmou o último pagamento); `recoveredBy`: quem reabriu o caixa nesse caso. */
  SaleCompleted: { operatorId: string | null; recoveredBy?: string | null; totalCents: Cents; paidCents: Cents; changeCents: Cents }
}

export type SaleEvent = { [K in SaleEventType]: SaleJournalEvent & { type: K; data: SaleEventData[K] } }[SaleEventType]

export const emptyBook = (): SaleBook => ({ currentId: null, sales: [] })

/** Mudar os itens tira o desconto no total (PDV-04). */
const NO_SALE_DISCOUNT = { discountCents: 0, discountReason: null } as const

const replace = (book: SaleBook, sale: Sale): Sale[] => book.sales.map((s) => (s.id === sale.id ? sale : s))

/** Aplica um evento (os de venda desconhecida, menos o `SaleStarted`, são ignorados: o diário pode ter sobra de uma versão antiga). */
export function applySaleEvent(book: SaleBook, event: SaleEvent): SaleBook {
  if (event.type === 'SaleStarted') {
    if (book.sales.some((s) => s.id === event.saleId)) return book
    const sale: Sale = { id: event.saleId, startedAt: event.at, status: 'open', parkedAt: null, lines: [], lastAdd: null, customer: null, inPayment: false, tenders: [] }
    return { currentId: event.saleId, sales: [...book.sales, sale] }
  }

  const sale = book.sales.find((s) => s.id === event.saleId)
  if (!sale) return book
  switch (event.type) {
    case 'LineAdded':
      return { ...book, sales: replace(book, { ...sale, ...NO_SALE_DISCOUNT, lines: [...sale.lines, event.data.line], lastAdd: { lineId: event.data.line.lineId, quantity: event.data.line.quantity } }) }
    case 'LineChanged': {
      const { lineId, quantity, note, reason } = event.data
      // PDV-04: mudar a quantidade tira o desconto da linha e o do total.
      const lines = sale.lines.map((l) => (l.lineId === lineId
        ? { ...l, ...(quantity !== undefined ? { quantity, discountCents: 0, discountReason: null } : {}), ...(note !== undefined ? { note } : {}) }
        : l))
      const lastAdd = reason === 'same-item' && quantity !== undefined
        ? { lineId, quantity: subtractQuantities(quantity, sale.lines.find((l) => l.lineId === lineId)?.quantity ?? '0') }
        : reason === 'remove-last' || (reason === 'quantity' && sale.lastAdd?.lineId === lineId) ? null : sale.lastAdd
      return { ...book, sales: replace(book, { ...sale, ...(quantity !== undefined ? NO_SALE_DISCOUNT : {}), lines, lastAdd }) }
    }
    case 'LineRemoved':
      return { ...book, sales: replace(book, { ...sale, ...NO_SALE_DISCOUNT, lines: sale.lines.filter((l) => l.lineId !== event.data.lineId), lastAdd: null }) }
    case 'DiscountApplied': {
      const { lineId, discountCents, reason } = event.data
      const text = discountCents > 0 ? reason : null
      if (lineId === null) return { ...book, sales: replace(book, { ...sale, discountCents, discountReason: text }) }
      const lines = sale.lines.map((l) => (l.lineId === lineId ? { ...l, discountCents, discountReason: text } : l))
      return { ...book, sales: replace(book, { ...sale, ...NO_SALE_DISCOUNT, lines }) }
    }
    case 'PriceOverridden': {
      const { lineId, unitCents, reason } = event.data
      const lines = sale.lines.map((l) => (l.lineId === lineId
        ? { ...l, listUnitCents: l.listUnitCents ?? l.unitCents, unitCents, priceReason: reason, discountCents: 0, discountReason: null }
        : l))
      return { ...book, sales: replace(book, { ...sale, ...NO_SALE_DISCOUNT, lines }) }
    }
    case 'SaleParked':
      return { currentId: book.currentId === sale.id ? null : book.currentId, sales: replace(book, { ...sale, status: 'parked', parkedAt: event.at }) }
    case 'SaleResumed':
      return { currentId: sale.id, sales: replace(book, { ...sale, status: 'open', parkedAt: null }) }
    case 'SaleDiscarded':
      return { currentId: book.currentId === sale.id ? null : book.currentId, sales: book.sales.filter((s) => s.id !== sale.id) }
    case 'CustomerSet':
      return { ...book, sales: replace(book, { ...sale, customer: event.data.customer }) }
    case 'PaymentStarted':
      return { ...book, sales: replace(book, { ...sale, inPayment: true }) }
    case 'PaymentCancelled':
      return { ...book, sales: replace(book, { ...sale, inPayment: false }) }
    case 'TenderCaptured':
    case 'TenderChanged': {
      const tender = event.data.tender
      const tenders = sale.tenders.some((t) => t.tenderId === tender.tenderId)
        ? sale.tenders.map((t) => (t.tenderId === tender.tenderId ? tender : t))
        : [...sale.tenders, tender]
      return { ...book, sales: replace(book, { ...sale, tenders }) }
    }
    case 'SaleCompleted':
      return { currentId: book.currentId === sale.id ? null : book.currentId, sales: book.sales.filter((s) => s.id !== sale.id) }
  }
}

/** Refaz o livro de vendas a partir do diário (CA 18). */
export function replaySales(events: readonly SaleJournalEvent[]): SaleBook {
  return events.reduce((book, e) => applySaleEvent(book, e as SaleEvent), emptyBook())
}

export const currentSale = (book: SaleBook): Sale | null => book.sales.find((s) => s.id === book.currentId) ?? null
export const parkedSales = (book: SaleBook): Sale[] => book.sales.filter((s) => s.status === 'parked')

/** Valor bruto da linha (preço praticado × quantidade), antes do desconto. */
export const lineGross = (line: SaleLine): Cents => lineTotalCents(line.unitCents, line.quantity)
/** Valor da linha com o desconto dela (PDV-04). */
export const lineTotal = (line: SaleLine): Cents => lineGross(line) - (line.discountCents ?? 0)
/** Soma das linhas, antes do desconto no total. */
export const saleSubtotal = (sale: Sale | null): Cents => (sale ? sumCents(sale.lines.map(lineTotal)) : 0)
/** Total a pagar: as linhas menos o desconto no total. */
export const saleTotal = (sale: Sale | null): Cents => (sale ? saleSubtotal(sale) - (sale.discountCents ?? 0) : 0)

/**
 * PDV-04 CA 1 e 2: o desconto passa do limite da loja (em %)? Em centavos, sem arredondar o percentual (desconto × 100 > limite × base), o mesmo
 * cálculo do servidor. Base zero com desconto passa sempre.
 */
export const discountAboveLimit = (discountCents: Cents, baseCents: Cents, limitPercent: number): boolean =>
  discountCents > 0 && (baseCents <= 0 || discountCents * 100 > limitPercent * baseCents)

/** Desconto em % sobre a base, em centavos (meio centavo para cima), o mesmo arredondamento do servidor. */
export const percentToCents = (baseCents: Cents, percent: number): Cents => Math.floor((baseCents * Math.round(percent * 100) + 5000) / 10000)

/**
 * Rateio do desconto no total pelas linhas (o mesmo do servidor): proporcional ao valor de cada uma, os centavos que sobram pelos maiores restos
 * (empate pela ordem). A soma das partes é sempre o desconto; linha de valor zero não recebe parte.
 */
export function prorateCents(nets: readonly Cents[], discount: Cents): Cents[] {
  const shares = nets.map(() => 0)
  const total = nets.filter((n) => n > 0).reduce((a, b) => a + b, 0)
  if (discount <= 0 || total <= 0) return shares
  const rests = nets.map((n, i) => {
    if (n <= 0) return { i, rest: -1 }
    const exact = (discount * n) / total
    shares[i] = Math.floor(exact)
    return { i, rest: exact - shares[i] }
  })
  let left = discount - shares.reduce((a, b) => a + b, 0)
  for (const { i } of rests.filter((r) => r.rest >= 0).sort((a, b) => b.rest - a.rest || a.i - b.i)) {
    if (left <= 0) break
    shares[i]++
    left--
  }
  return shares
}
