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
 * Todo evento com dinheiro leva o `operatorId` (no pagamento, o `confirmedBy`).
 */
export type SaleEventType = 'SaleStarted' | 'LineAdded' | 'LineChanged' | 'LineRemoved' | 'SaleParked' | 'SaleResumed' | 'SaleDiscarded' | 'CustomerSet'
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
  LineChanged: { lineId: string; quantity?: string; note?: string | null; reason: LineChangeReason; operatorId: string | null }
  LineRemoved: { lineId: string; reason: LineRemoveReason; operatorId: string | null }
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
      return { ...book, sales: replace(book, { ...sale, lines: [...sale.lines, event.data.line], lastAdd: { lineId: event.data.line.lineId, quantity: event.data.line.quantity } }) }
    case 'LineChanged': {
      const { lineId, quantity, note, reason } = event.data
      const lines = sale.lines.map((l) => (l.lineId === lineId ? { ...l, ...(quantity !== undefined ? { quantity } : {}), ...(note !== undefined ? { note } : {}) } : l))
      const lastAdd = reason === 'same-item' && quantity !== undefined
        ? { lineId, quantity: subtractQuantities(quantity, sale.lines.find((l) => l.lineId === lineId)?.quantity ?? '0') }
        : reason === 'remove-last' || (reason === 'quantity' && sale.lastAdd?.lineId === lineId) ? null : sale.lastAdd
      return { ...book, sales: replace(book, { ...sale, lines, lastAdd }) }
    }
    case 'LineRemoved':
      return { ...book, sales: replace(book, { ...sale, lines: sale.lines.filter((l) => l.lineId !== event.data.lineId), lastAdd: null }) }
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

export const lineTotal = (line: SaleLine): Cents => lineTotalCents(line.unitCents, line.quantity)
export const saleTotal = (sale: Sale | null): Cents => (sale ? sumCents(sale.lines.map(lineTotal)) : 0)
