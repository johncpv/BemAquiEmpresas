import type { LocalStore } from '@/pdv/local-store/local-store'
import type { CatalogItem } from '@/pdv/local-store/catalog-types'
import type { CustomerDocumentRecord } from '@/pdv/local-store/sale-journal-types'
import type { Cents } from '@/pdv/money'
import { canTransition, changeCents, CARD_NUMBER_MESSAGE, looksLikeCardNumber, paidCents, remainingCents, type Tender } from '@/pdv/payment/payment-model'
import { uuidv7 } from '@/pdv/uuid-v7'
import { parseCodeInput } from './code-input'
import { checkCustomerDocument, type DocumentCipher } from './customer-document'
import { addQuantities, packQuantityText, parseQuantity, subtractQuantities, toMilli } from './quantity'
import { checkStockMessage, type StockGuard } from '@/pdv/stock/stock-guard'
import {
  applySaleEvent, currentSale, discountAboveLimit, emptyBook, lineGross, parkedSales, replaySales, type Sale, type SaleAuthorization, saleSubtotal, saleTotal, type SaleBook,
  type SaleCustomer, type SaleEvent, type SaleEventData, type SaleEventType, type SaleLine,
} from './sale-model'

/** Quem autorizou, no evento (PDV-04): o id do supervisor ou do próprio operador e, com o PIN do supervisor, a aprovação. */
const authData = (auth: SaleAuthorization) => ({ authorizedBy: auth.authorizedBy, ...(auth.approvalId ? { approvalId: auth.approvalId } : {}) })

/** Ajustes do caixa que mudam a tela de venda (guardados no aparelho, `pdv.settings`). */
export interface SaleSettings {
  /** Toque (grade e busca) ou Teclado/leitor (foco no campo de código). O padrão vem do caixa (PDV-01, `uiMode`). */
  mode: 'touch' | 'keyboard'
  /** O mesmo item lançado de novo soma na linha (CA 5); desligado, cada lançamento é uma linha. */
  mergeSameItem: boolean
  /** O último item lançado sai sem supervisor (CA 10). A loja pode desligar. */
  freeRemoveLast: boolean
  /** PDV-04 (PP-38): desconto máximo da loja, em %, sem `pos.discount` nem supervisor. Vem da loja, nunca do aparelho; ausente = 0. */
  discountLimitPercent?: number
}

/** Fila de saída do cancelamento da venda concluída (PDV-04 CA 6 a 9). */
export const SALE_CANCEL_OUTBOX_KIND = 'pos.sale_cancel'

/** Cancelamento da venda concluída da sessão (PDV-04): a aprovação do supervisor e, com cartão, o estorno confirmado na maquininha. */
export interface SaleCancellation {
  saleId: string
  number: string
  cashSessionId: string | null
  reason: string
  supervisorId: string
  approvalId: string
  refundConfirmed: boolean
  refundNsu: string | null
}

export const SETTINGS_META = 'pdv.settings'
export const DEFAULT_SETTINGS: Omit<SaleSettings, 'mode'> = { mergeSameItem: true, freeRemoveLast: true }

/** Fila de saída: a venda fechada (descartada ou concluída) sobe como uma entrada só. */
export const SALE_OUTBOX_KIND = 'pos.sale'

export type AddResult =
  | { kind: 'added'; line: SaleLine; merged: boolean }
  | { kind: 'not-found'; code: string }
  | { kind: 'no-price'; item: CatalogItem }
  | { kind: 'needs-quantity'; item: CatalogItem; code: string | null }
  | { kind: 'invalid-quantity'; message: string }
  | { kind: 'search'; text: string }
  | { kind: 'locked'; message: string }
  /** PDV-05: a empresa não vende sem saldo e o último saldo recebido não cobre a quantidade. */
  | { kind: 'no-stock'; message: string }

export type RemoveResult = { kind: 'removed' } | { kind: 'needs-authorization' } | { kind: 'locked' }

/** Na tela de pagamento a venda não muda de itens (PDV-03): volta aos itens primeiro (só sem pagamento capturado). */
export const LOCKED_MESSAGE = 'A venda está no pagamento. Volte aos itens para mudar a venda.'

/** Resultado de um pagamento (PDV-03): capturado com saldo, venda concluída, ou recusado com o motivo. */
export type TenderResult =
  | { kind: 'recorded'; tender: Tender; remainingCents: Cents }
  | { kind: 'completed'; tender: Tender; number: string; totalCents: Cents; changeCents: Cents; tenders: Tender[] }
  | { kind: 'refused'; message: string }

/**
 * Venda concluída para a tela e o cupom (IMP-01): número, total, troco, pagamentos e, para montar o recibo no aparelho, os itens, o cliente (o
 * documento só pelo id do registro cifrado), o instante e quem fechou.
 */
export interface CompletedSale {
  number: string
  totalCents: Cents
  changeCents: Cents
  tenders: Tender[]
  saleId?: string
  completedAt?: string
  operatorId?: string | null
  lines?: SaleLine[]
  customer?: SaleCustomer | null
  /** PDV-04: desconto no total (o cupom rateia nas linhas). */
  discountCents?: Cents
}

/** Pagamento capturado é fato (revisão de segurança): só sai por estorno ou cancelamento, com quem autorizou (método próprio da PDV-04). */
export const CAPTURED_LOCKED_MESSAGE = 'Este pagamento já foi recebido. Ele só sai por estorno ou cancelamento, com o supervisor.'

export interface SaleRegisterOptions {
  store: LocalStore
  /** Quem está no caixa (vai nos eventos de remoção e descarte). */
  operatorId: string | null
  /** Prefixo do número da venda deste caixa (`{loja}-{caixa}-`, do aperto de mão). */
  salePrefix: string
  settings: () => SaleSettings
  cipher?: DocumentCipher | null
  now?: () => Date
  /** Turno (sessão de caixa) em que a venda conclui (CX-01): vai na entrada da fila de saída para a OFF-02 e a CX-03 juntarem as vendas da sessão. */
  cashSessionId?: () => string | null
  /** PDV-05 (PP-02): confere o saldo da loja antes de lançar ou aumentar um item (ausente: não confere). */
  stock?: StockGuard
}

/** Regra da venda que a tela mostra ao operador (não é erro de gravação). */
export class SaleRuleError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'SaleRuleError'
  }
}

/** Número da venda `{loja}-{caixa}-{seq}`, atribuído só quando a venda é concluída ou descartada. */
export const saleNumber = (prefix: string, seq: number) => `${prefix}${String(seq).padStart(6, '0')}`

/**
 * A venda do caixa (PDV-02): lança, muda, remove, põe em espera, retoma, descarta e liga o cliente. Toda ação vira evento gravado no
 * `LocalStore` **antes** de a tela mudar; só depois o estado em memória avança e a tela é avisada. As ações entram em fila (uma de cada vez),
 * para duas leituras seguidas do leitor nunca se atropelarem. Nada aqui fala com a rede.
 */
export class SaleRegister {
  private book: SaleBook = emptyBook()
  private readonly listeners = new Set<() => void>()
  private queue: Promise<unknown> = Promise.resolve()
  private readonly options: SaleRegisterOptions
  /** Vendas concluídas ao reabrir (a queda foi entre o último pagamento e o fechamento): a tela avisa o número e o troco. */
  readonly recovered: CompletedSale[] = []

  private constructor(options: SaleRegisterOptions) {
    this.options = options
  }

  /** Abre o caixa com o que estava no diário (CA 18: a venda volta como estava). */
  static async open(options: SaleRegisterOptions): Promise<SaleRegister> {
    const register = new SaleRegister(options)
    const journal = await options.store.saleJournal()
    register.book = replaySales(journal)
    // Sobra de uma queda entre gravar o último pagamento e fechar a venda: a soma fecha o total exato, então conclui agora. Quem fechou é quem
    // confirmou o último pagamento (`confirmedBy`); quem reabriu fica em `recoveredBy`. Pago acima do total não conclui: fica para conferência.
    for (const sale of [...register.book.sales]) {
      const paid = paidCents(sale.tenders)
      if (!sale.inPayment || paid <= 0 || paid !== saleTotal(sale)) continue
      const last = journal.filter((e) => e.saleId === sale.id && e.type === 'TenderCaptured').at(-1)
      const confirmedBy = (last?.data as SaleEventData['TenderCaptured'] | undefined)?.tender.confirmedBy ?? null
      register.recovered.push(await register.finish(sale, [], { operatorId: confirmedBy ?? options.operatorId, recoveredBy: options.operatorId }))
    }
    return register
  }

  readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  readonly getSnapshot = () => this.book

  current(): Sale | null { return currentSale(this.book) }
  parked(): Sale[] { return parkedSales(this.book) }

  private now(): string { return (this.options.now?.() ?? new Date()).toISOString() }

  private event<K extends SaleEventType>(type: K, saleId: string, data: SaleEventData[K], at = this.now()): SaleEvent {
    return { id: uuidv7(Date.parse(at)), saleId, type, at, data } as SaleEvent
  }

  /** Grava e só então aplica (uma ação de cada vez). */
  private run<T>(action: () => Promise<T>): Promise<T> {
    const next = this.queue.then(action, action)
    this.queue = next.catch(() => undefined)
    return next
  }

  private async commit(events: SaleEvent[]): Promise<void> {
    await this.options.store.appendSaleEvents(events)
    this.book = events.reduce(applySaleEvent, this.book)
    for (const l of this.listeners) l()
  }

  /** O texto do campo de código (CA 1, 2, 3 e 6): `3*789…`, código puro ou texto de busca. */
  addFromInput(raw: string): Promise<AddResult> {
    const input = parseCodeInput(raw)
    if (!input.isCode) return Promise.resolve({ kind: 'search', text: input.text })
    const found = this.options.store.lookupBarcode(input.text)
    if (!found) return Promise.resolve({ kind: 'not-found', code: input.text })
    const quantity = input.quantity ?? (found.item.fractional && found.barcode.packQuantity === undefined ? null : packQuantityText(found.barcode.packQuantity, found.item.fractional))
    return this.addItem(found.item, quantity, input.text)
  }

  /** Lança o item (da leitura, da busca ou da grade). Sem quantidade, pesável pede a quantidade; por unidade entra 1. */
  addItem(item: CatalogItem, quantityText: string | null, code: string | null = null): Promise<AddResult> {
    return this.run(async () => {
      const price = this.options.store.priceOf(item.id)
      if (this.current()?.inPayment) return { kind: 'locked', message: LOCKED_MESSAGE }
      if (!price) return { kind: 'no-price', item }
      if (quantityText === null && item.fractional) return { kind: 'needs-quantity', item, code }
      const parsed = parseQuantity(quantityText ?? '1', item.fractional)
      if (!parsed.ok) return { kind: 'invalid-quantity', message: parsed.message }
      const inSale = (this.current()?.lines ?? []).filter((l) => l.itemId === item.id).reduce((sum, l) => sum + (toMilli(l.quantity) ?? 0), 0)
      const refused = await checkStockMessage(this.options.stock, item, inSale + (toMilli(parsed.value) ?? 0))
      if (refused) return { kind: 'no-stock', message: refused }

      const events: SaleEvent[] = []
      let sale = this.current()
      if (!sale) {
        const started = this.event('SaleStarted', uuidv7(), { operatorId: this.options.operatorId })
        events.push(started)
        sale = { id: started.saleId, startedAt: started.at, status: 'open', parkedAt: null, lines: [], lastAdd: null, customer: null, inPayment: false, tenders: [] }
      }

      const same = this.options.settings().mergeSameItem ? sale.lines.find((l) => l.itemId === item.id && l.unitCents === price.priceCents && !l.note) : undefined
      if (same) {
        const quantity = addQuantities(same.quantity, parsed.value)
        events.push(this.event('LineChanged', sale.id, { lineId: same.lineId, quantity, reason: 'same-item', operatorId: this.options.operatorId }))
        await this.commit(events)
        return { kind: 'added', line: { ...same, quantity }, merged: true }
      }

      const line: SaleLine = {
        lineId: uuidv7(), itemId: item.id, name: item.name, unit: item.unit, fractional: item.fractional, quantity: parsed.value,
        unitCents: price.priceCents, priceTableVersion: price.tableVersion, code, note: null,
      }
      events.push(this.event('LineAdded', sale.id, { line }))
      await this.commit(events)
      return { kind: 'added', line, merged: false }
    })
  }

  /**
   * CA 9: a quantidade nova da linha (pesável com 3 casas; por unidade, inteiro). Diminuir segue a regra da remoção (CA 10, revisão de
   * segurança, achado 3): sem `pos.cancel_item` (`authorized`), só desfaz até o último lançamento da linha; reduzir uma linha antiga, ou abaixo
   * do último lançamento, pede autorização. Toda mudança fica registrada com quem estava no caixa.
   */
  setQuantity(lineId: string, text: string, authorized: boolean | SaleAuthorization | null = false): Promise<{ ok: true } | { ok: false; message: string; needsAuthorization?: true }> {
    const auth = this.authOf(authorized)
    return this.run(async () => {
      const sale = this.current()
      const line = sale?.lines.find((l) => l.lineId === lineId)
      if (!sale || !line) return { ok: false, message: 'A linha não está mais na venda.' }
      if (sale.inPayment) return { ok: false, message: LOCKED_MESSAGE }
      const parsed = parseQuantity(text, line.fractional)
      if (!parsed.ok) return parsed
      if (parsed.value === line.quantity) return { ok: true }
      const others = sale.lines.filter((l) => l.itemId === line.itemId && l.lineId !== lineId).reduce((sum, l) => sum + (toMilli(l.quantity) ?? 0), 0)
      const wanted = others + (toMilli(parsed.value) ?? 0)
      if ((toMilli(parsed.value) ?? 0) > (toMilli(line.quantity) ?? 0)) {
        const refused = await checkStockMessage(this.options.stock, { id: line.itemId, name: line.name, unit: line.unit }, wanted)
        if (refused) return { ok: false, message: refused }
      }
      if (!auth && !this.isFreeReduction(sale, line, parsed.value)) {
        return { ok: false, needsAuthorization: true, message: 'Diminuir um item que não é o último lançado pede autorização. Chame o supervisor.' }
      }

      const free = this.isFreeReduction(sale, line, parsed.value)
      await this.commit([this.event('LineChanged', sale.id, { lineId, quantity: parsed.value, reason: 'quantity', operatorId: this.options.operatorId, ...(free || !auth ? {} : authData(auth)) })])
      return { ok: true }
    })
  }

  /** `true` = o próprio operador, com a permissão; a aprovação do supervisor vem como objeto; `false`/null = sem autorização. */
  private authOf(authorized: boolean | SaleAuthorization | null): SaleAuthorization | null {
    if (authorized === true) return this.options.operatorId ? { authorizedBy: this.options.operatorId } : null
    return authorized || null
  }

  /** PDV-04 CA 1 e 2: o desconto passa do limite da loja e pede `pos.discount` ou o supervisor? */
  discountNeedsAuthorization(lineId: string | null, discountCents: Cents): boolean {
    const sale = this.current()
    if (!sale) return false
    const line = lineId ? sale.lines.find((l) => l.lineId === lineId) : null
    const base = line ? lineGross(line) : saleSubtotal(sale)
    return discountAboveLimit(discountCents, base, this.options.settings().discountLimitPercent ?? 0)
  }

  /**
   * PDV-04 CA 1 e 2: desconto em centavos na linha ou no total (zero tira), com o motivo. Até o limite da loja passa; acima, só com a autorização (o
   * operador com `pos.discount` ou o supervisor). Um desconto novo substitui o anterior do mesmo alvo.
   */
  applyDiscount(lineId: string | null, discountCents: Cents, reason: string | null, authorized: boolean | SaleAuthorization | null = null): Promise<{ ok: true } | { ok: false; message: string; needsAuthorization?: true }> {
    const auth = this.authOf(authorized)
    return this.run(async () => {
      const sale = this.current()
      if (!sale || sale.lines.length === 0) return { ok: false, message: 'Lance um item antes de dar desconto.' }
      if (sale.inPayment) return { ok: false, message: LOCKED_MESSAGE }
      const line = lineId ? sale.lines.find((l) => l.lineId === lineId) : null
      if (lineId && !line) return { ok: false, message: 'A linha não está mais na venda.' }
      const base = line ? lineGross(line) : saleSubtotal(sale)
      if (!Number.isInteger(discountCents) || discountCents < 0 || discountCents > base) {
        return { ok: false, message: line ? 'O desconto passa do valor do item.' : 'O desconto passa do valor da venda.' }
      }
      const text = reason?.trim().slice(0, 140) || null
      if (discountCents > 0 && !text) return { ok: false, message: 'Escolha o motivo do desconto.' }
      if (text && looksLikeCardNumber(text)) return { ok: false, message: CARD_NUMBER_MESSAGE }
      const above = discountAboveLimit(discountCents, base, this.options.settings().discountLimitPercent ?? 0)
      if (above && !auth) return { ok: false, needsAuthorization: true, message: 'Desconto acima do limite da loja. Chame o supervisor.' }
      await this.commit([this.event('DiscountApplied', sale.id, {
        lineId: line?.lineId ?? null, discountCents, reason: discountCents > 0 ? text : null, operatorId: this.options.operatorId, ...(above && auth ? authData(auth) : {}),
      })])
      return { ok: true }
    })
  }

  /** PDV-04 CA 3: troca do preço da linha, sempre com a autorização (o operador com `pos.change_price` ou o supervisor) e o motivo. */
  overridePrice(lineId: string, unitCents: Cents, reason: string, authorized: boolean | SaleAuthorization | null): Promise<{ ok: true } | { ok: false; message: string; needsAuthorization?: true }> {
    const auth = this.authOf(authorized)
    return this.run(async () => {
      const sale = this.current()
      const line = sale?.lines.find((l) => l.lineId === lineId)
      if (!sale || !line) return { ok: false, message: 'A linha não está mais na venda.' }
      if (sale.inPayment) return { ok: false, message: LOCKED_MESSAGE }
      if (!Number.isInteger(unitCents) || unitCents < 0 || unitCents > 999_999_999) return { ok: false, message: 'Informe o preço novo.' }
      if (unitCents === line.unitCents) return { ok: true }
      const text = reason.trim().slice(0, 140)
      if (!text) return { ok: false, message: 'Escolha o motivo da troca de preço.' }
      if (looksLikeCardNumber(text)) return { ok: false, message: CARD_NUMBER_MESSAGE }
      if (!auth) return { ok: false, needsAuthorization: true, message: 'Trocar o preço pede autorização. Chame o supervisor.' }
      await this.commit([this.event('PriceOverridden', sale.id, { lineId, unitCents, reason: text, operatorId: this.options.operatorId, ...authData(auth) })])
      return { ok: true }
    })
  }

  /**
   * PDV-04 CA 6 a 9: cancela a venda concluída desta sessão. Vai para a fila de saída com a aprovação do supervisor (que já conferiu, com rede, que
   * a venda é deste caixa e da sessão aberta); no servidor, o dinheiro sai da gaveta, o estoque volta e o Proprietário é avisado.
   */
  async cancelCompleted(input: SaleCancellation): Promise<void> {
    const reason = input.reason.trim().slice(0, 140)
    if (!reason) throw new SaleRuleError('Escolha o motivo do cancelamento.')
    if (looksLikeCardNumber(reason)) throw new SaleRuleError(CARD_NUMBER_MESSAGE)
    const nsu = input.refundNsu?.trim() || null
    if (nsu && (nsu.length > 12 || looksLikeCardNumber(nsu))) throw new SaleRuleError('Informe o NSU do estorno (até 12 caracteres).')
    await this.options.store.enqueue(SALE_CANCEL_OUTBOX_KIND, {
      saleId: input.saleId, number: input.number, cashSessionId: input.cashSessionId, at: this.now(), operatorId: this.options.operatorId,
      authorizedBy: input.supervisorId, approvalId: input.approvalId, reason, refundConfirmed: input.refundConfirmed, refundNsu: nsu,
    })
  }

  /** Aumentar é livre; diminuir, só até o que havia antes do último lançamento e com a remoção livre ligada (a mesma regra do CA 10). */
  private isFreeReduction(sale: Sale, line: SaleLine, quantity: string): boolean {
    const next = toMilli(quantity) ?? 0
    const now = toMilli(line.quantity) ?? 0
    if (next >= now) return true
    if (!this.isFreeRemoval(line.lineId) || !sale.lastAdd) return false
    return next >= now - (toMilli(sale.lastAdd.quantity) ?? 0)
  }

  /** CA 11: observação da linha (sai no cupom). Vazia apaga. */
  setNote(lineId: string, note: string): Promise<void> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale?.lines.some((l) => l.lineId === lineId) || sale.inPayment) return
      const text = note.trim().slice(0, 140) || null
      await this.commit([this.event('LineChanged', sale.id, { lineId, note: text, reason: 'note', operatorId: this.options.operatorId })])
    })
  }

  /** É o último lançamento, que sai sem supervisor (CA 10)? */
  isFreeRemoval(lineId: string): boolean {
    const sale = this.current()
    return !!sale && this.options.settings().freeRemoveLast && sale.lastAdd?.lineId === lineId
  }

  /**
   * CA 10: o último item lançado sai sem supervisor (se a linha somou o mesmo item, sai só o último lançamento); outra linha exige
   * `pos.cancel_item` do operador (`true`) ou a aprovação do supervisor (PDV-04), com o motivo. Toda remoção fica registrada com quem estava no caixa.
   */
  removeLine(lineId: string, authorized: boolean | SaleAuthorization | null, motive: string | null = null): Promise<RemoveResult> {
    const auth = this.authOf(authorized)
    return this.run(async () => {
      const sale = this.current()
      const line = sale?.lines.find((l) => l.lineId === lineId)
      if (!sale || !line) return { kind: 'removed' }
      if (sale.inPayment) return { kind: 'locked' }
      const operatorId = this.options.operatorId
      if (this.isFreeRemoval(lineId) && sale.lastAdd) {
        const rest = subtractQuantities(line.quantity, sale.lastAdd.quantity)
        await this.commit([(toMilli(rest) ?? 0) > 0
          ? this.event('LineChanged', sale.id, { lineId, quantity: rest, reason: 'remove-last', operatorId })
          : this.event('LineRemoved', sale.id, { lineId, reason: 'last-item', operatorId })])
        return { kind: 'removed' }
      }

      if (!auth) return { kind: 'needs-authorization' }
      const text = motive?.trim().slice(0, 140) || null
      await this.commit([this.event('LineRemoved', sale.id, { lineId, reason: 'authorized', operatorId, ...authData(auth), ...(text && !looksLikeCardNumber(text) ? { motive: text } : {}) })])
      return { kind: 'removed' }
    })
  }

  /** CA 12: a venda vai para a lista de espera e a tela fica livre para a próxima. */
  park(): Promise<boolean> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale || sale.lines.length === 0 || sale.inPayment) return false
      await this.commit([this.event('SaleParked', sale.id, {})])
      return true
    })
  }

  /** CA 12: traz a venda em espera de volta. A que está na tela (com itens) vai para a espera no lugar dela. */
  resume(saleId: string): Promise<void> {
    return this.run(async () => {
      const target = this.book.sales.find((s) => s.id === saleId && s.status === 'parked')
      if (!target) return
      const events: SaleEvent[] = []
      const sale = this.current()
      if (sale?.inPayment) return
      if (sale && sale.lines.length > 0) events.push(this.event('SaleParked', sale.id, {}))
      else if (sale) await this.close(sale, 'Venda sem itens trocada por uma venda em espera.')
      events.push(this.event('SaleResumed', saleId, {}))
      await this.commit(events)
    })
  }

  /** CA 13: descarta com o motivo; a venda ganha o número e sobe registrada. Sem `saleId`, a venda da tela. */
  discard(reason: string, saleId?: string): Promise<{ number: string } | null> {
    return this.run(async () => {
      const sale = saleId ? this.book.sales.find((s) => s.id === saleId) : this.current()
      if (!sale) return null
      if (paidCents(sale.tenders) > 0) throw new SaleRuleError('A venda tem pagamento recebido: o descarte pede o estorno com supervisor.')
      return { number: await this.close(sale, reason) }
    })
  }

  private async close(sale: Sale, reason: string): Promise<string> {
    const text = reason.trim()
    if (!text) throw new SaleRuleError('Informe o motivo do descarte.')
    const discarded = this.event('SaleDiscarded', sale.id, { reason: text, operatorId: this.options.operatorId })
    const prefix = this.options.salePrefix
    const { number } = await this.options.store.closeSale(sale.id, (seq, events) => ({
      kind: SALE_OUTBOX_KIND,
      at: discarded.at,
      payload: { saleId: sale.id, number: saleNumber(prefix, seq), status: 'discarded', reason: text, events: [...events, discarded] },
    }), sale.customer?.documentId ?? null)
    this.book = applySaleEvent(this.book, discarded)
    for (const l of this.listeners) l()
    return saleNumber(prefix, number)
  }

  /**
   * CA 14 e 15: o cliente da venda. `document` (CPF/CNPJ avulso) é conferido e gravado cifrado num registro à parte; o evento leva só o id do
   * registro. `null` em tudo tira o cliente (e apaga o documento guardado).
   */
  setCustomer(input: { customerId?: string | null; customerName?: string | null; document?: string | null }): Promise<{ ok: true } | { ok: false; message: string }> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale) return { ok: false, message: 'Lance um item antes de informar o cliente.' }
      const store = this.options.store
      const previous = sale.customer?.documentId ?? null
      let documentId: string | null = previous
      let documentKind = sale.customer?.documentKind ?? null
      if (input.document !== undefined) {
        documentId = null
        documentKind = null
        if (input.document) {
          const checked = checkCustomerDocument(input.document)
          if (!checked.ok) return checked
          if (!this.options.cipher) return { ok: false, message: 'Este aparelho não consegue guardar o documento com segurança.' }
          const { iv, cipher } = await this.options.cipher.encrypt(checked.value)
          const record: CustomerDocumentRecord = { id: uuidv7(), saleId: sale.id, kind: checked.kind, iv, cipher, createdAt: this.now() }
          await store.putCustomerDocument(record)
          documentId = record.id
          documentKind = checked.kind
        }
      }

      const customerId = input.customerId !== undefined ? input.customerId : sale.customer?.customerId ?? null
      const customerName = input.customerName !== undefined ? input.customerName : sale.customer?.customerName ?? null
      const customer: SaleCustomer | null = customerId || documentId ? { customerId, customerName, documentId, documentKind } : null
      await this.commit([this.event('CustomerSet', sale.id, { customer })])
      if (previous && previous !== documentId) await store.deleteCustomerDocument(previous)
      return { ok: true }
    })
  }

  /** CA 1: "Pagar" leva a venda para `InPayment`, com o total daquele instante. Venda sem itens ou de valor zero não vai. */
  startPayment(): Promise<{ ok: true } | { ok: false; message: string }> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale || sale.lines.length === 0) return { ok: false, message: 'Lance um item antes de pagar.' }
      if (sale.inPayment) return { ok: true }
      const total = saleTotal(sale)
      if (total <= 0) return { ok: false, message: 'A venda está com total zero.' }
      await this.commit([this.event('PaymentStarted', sale.id, { operatorId: this.options.operatorId, totalCents: total })])
      return { ok: true }
    })
  }

  /** CA 7 e 8: "Voltar aos itens" só sem pagamento capturado; com algo capturado, só por estorno com supervisor (PDV-04). */
  cancelPayment(): Promise<{ ok: true } | { ok: false; message: string }> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale?.inPayment) return { ok: true }
      if (paidCents(sale.tenders) > 0) {
        return { ok: false, message: 'Já há pagamento recebido nesta venda. Ele só sai por estorno, com o supervisor; até lá, receba o que falta.' }
      }
      await this.commit([this.event('PaymentCancelled', sale.id, { operatorId: this.options.operatorId })])
      return { ok: true }
    })
  }

  /**
   * Grava o pagamento que a porta `PaymentTerminal` devolveu (CA 2 a 7 e 10). Confere a invariante: o capturado nunca passa do que falta; troco
   * só no dinheiro; nada que pareça número de cartão. Quando a soma dos capturados fecha o total, conclui a venda na mesma transação (CA 6):
   * número `{loja}-{caixa}-{seq}` e a venda inteira na fila de saída (a subida é da OFF-02).
   */
  recordTender(tender: Tender): Promise<TenderResult> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale?.inPayment) return { kind: 'refused', message: 'A venda não está no pagamento.' }
      const refused = this.checkTender(sale, tender)
      if (refused) return { kind: 'refused', message: refused }
      const recorded: Tender = { ...tender, confirmedBy: this.options.operatorId }
      const total = saleTotal(sale)

      if (recorded.status !== 'Captured') {
        await this.commit([this.event('TenderChanged', sale.id, { tender: recorded })])
        return { kind: 'recorded', tender: recorded, remainingCents: remainingCents(total, sale.tenders) }
      }

      const captured = this.event('TenderCaptured', sale.id, { tender: recorded })
      const tenders = applySaleEvent(this.book, captured).sales.find((s) => s.id === sale.id)?.tenders ?? []
      if (paidCents(tenders) < total) {
        await this.commit([captured])
        return { kind: 'recorded', tender: recorded, remainingCents: remainingCents(total, tenders) }
      }

      const done = await this.finish({ ...sale, tenders }, [captured])
      return { kind: 'completed', tender: recorded, ...done }
    })
  }

  private checkTender(sale: Sale, tender: Tender): string | null {
    const previous = sale.tenders.find((t) => t.tenderId === tender.tenderId)
    if (previous?.status === 'Captured') return CAPTURED_LOCKED_MESSAGE
    if (previous && !canTransition(previous.status, tender.status)) return 'Este pagamento já foi registrado.'
    if (!Number.isSafeInteger(tender.amountCents) || tender.amountCents <= 0) return 'Informe um valor maior que zero.'
    if (tender.status === 'Captured' && tender.amountCents > remainingCents(saleTotal(sale), sale.tenders)) return 'O valor passa do que falta.'
    if (tender.changeCents > 0 && tender.methodType !== 'Cash') return 'Só o dinheiro dá troco.'
    if ([tender.nsu, tender.note, tender.authorizationCode].some((v) => v && looksLikeCardNumber(v))) return CARD_NUMBER_MESSAGE
    return null
  }

  /**
   * Fecha a venda paga: `SaleCompleted` e a entrada `pos.sale` na fila, numa transação (o CPF fica no registro cifrado, fora da fila). Só com a
   * soma dos capturados igual ao total (acima do total não conclui: fica para conferência).
   */
  private async finish(sale: Sale, extra: SaleEvent[], by: { operatorId: string | null; recoveredBy: string | null } = { operatorId: this.options.operatorId, recoveredBy: null }): Promise<CompletedSale> {
    const total = saleTotal(sale)
    const paid = paidCents(sale.tenders)
    if (paid !== total) throw new SaleRuleError('A soma dos pagamentos não fecha o total da venda.')
    const change = changeCents(sale.tenders)
    const completed = this.event('SaleCompleted', sale.id, { ...by, totalCents: total, paidCents: paid, changeCents: change })
    const prefix = this.options.salePrefix
    const { number } = await this.options.store.closeSale(sale.id, (seq, events) => ({
      kind: SALE_OUTBOX_KIND,
      at: completed.at,
      payload: {
        saleId: sale.id, number: saleNumber(prefix, seq), status: 'completed', totalCents: total, changeCents: change,
        cashSessionId: this.options.cashSessionId?.() ?? null,
        events: [...events, ...extra, completed],
      },
    }))
    this.book = [...extra, completed].reduce(applySaleEvent, this.book)
    for (const l of this.listeners) l()
    return {
      number: saleNumber(prefix, number), totalCents: total, changeCents: change, tenders: sale.tenders,
      saleId: sale.id, completedAt: completed.at, operatorId: by.operatorId, lines: sale.lines, customer: sale.customer, discountCents: sale.discountCents ?? 0,
    }
  }

  /** IMP-01: o documento do consumidor decifrado só para o cupom (sai mascarado no papel; nunca vai para log nem fila). */
  async decryptDocument(documentId: string): Promise<string | null> {
    if (!this.options.cipher) return null
    const record = await this.options.store.getCustomerDocument(documentId)
    return record ? this.options.cipher.decrypt(record) : null
  }

  /** O documento da venda decifrado, só para mostrar na tela (nunca para log). */
  async customerDocument(sale: Sale): Promise<string | null> {
    const id = sale.customer?.documentId
    if (!id || !this.options.cipher) return null
    const record = await this.options.store.getCustomerDocument(id)
    return record ? this.options.cipher.decrypt(record) : null
  }
}
