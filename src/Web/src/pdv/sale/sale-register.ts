import type { LocalStore } from '@/pdv/local-store/local-store'
import type { CatalogItem } from '@/pdv/local-store/catalog-types'
import type { CustomerDocumentRecord } from '@/pdv/local-store/sale-journal-types'
import { uuidv7 } from '@/pdv/uuid-v7'
import { parseCodeInput } from './code-input'
import { checkCustomerDocument, type DocumentCipher } from './customer-document'
import { addQuantities, packQuantityText, parseQuantity, subtractQuantities, toMilli } from './quantity'
import { applySaleEvent, currentSale, emptyBook, parkedSales, replaySales, type Sale, type SaleBook, type SaleCustomer, type SaleEvent, type SaleEventData, type SaleEventType, type SaleLine } from './sale-model'

/** Ajustes do caixa que mudam a tela de venda (guardados no aparelho, `pdv.settings`). */
export interface SaleSettings {
  /** Toque (grade e busca) ou Teclado/leitor (foco no campo de código). O padrão vem do caixa (PDV-01, `uiMode`). */
  mode: 'touch' | 'keyboard'
  /** O mesmo item lançado de novo soma na linha (CA 5); desligado, cada lançamento é uma linha. */
  mergeSameItem: boolean
  /** O último item lançado sai sem supervisor (CA 10). A loja pode desligar. */
  freeRemoveLast: boolean
}

export const SETTINGS_META = 'pdv.settings'
export const DEFAULT_SETTINGS: Omit<SaleSettings, 'mode'> = { mergeSameItem: true, freeRemoveLast: true }

/** Fila de saída: a venda fechada (descartada aqui; concluída na PDV-03) sobe como uma entrada só. */
export const SALE_OUTBOX_KIND = 'pos.sale'

export type AddResult =
  | { kind: 'added'; line: SaleLine; merged: boolean }
  | { kind: 'not-found'; code: string }
  | { kind: 'no-price'; item: CatalogItem }
  | { kind: 'needs-quantity'; item: CatalogItem; code: string | null }
  | { kind: 'invalid-quantity'; message: string }
  | { kind: 'search'; text: string }

export type RemoveResult = { kind: 'removed' } | { kind: 'needs-authorization' }

export interface SaleRegisterOptions {
  store: LocalStore
  /** Quem está no caixa (vai nos eventos de remoção e descarte). */
  operatorId: string | null
  /** Prefixo do número da venda deste caixa (`{loja}-{caixa}-`, do aperto de mão). */
  salePrefix: string
  settings: () => SaleSettings
  cipher?: DocumentCipher | null
  now?: () => Date
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

  private constructor(options: SaleRegisterOptions) {
    this.options = options
  }

  /** Abre o caixa com o que estava no diário (CA 18: a venda volta como estava). */
  static async open(options: SaleRegisterOptions): Promise<SaleRegister> {
    const register = new SaleRegister(options)
    register.book = replaySales(await options.store.saleJournal())
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
      if (!price) return { kind: 'no-price', item }
      if (quantityText === null && item.fractional) return { kind: 'needs-quantity', item, code }
      const parsed = parseQuantity(quantityText ?? '1', item.fractional)
      if (!parsed.ok) return { kind: 'invalid-quantity', message: parsed.message }

      const events: SaleEvent[] = []
      let sale = this.current()
      if (!sale) {
        const started = this.event('SaleStarted', uuidv7(), { operatorId: this.options.operatorId })
        events.push(started)
        sale = { id: started.saleId, startedAt: started.at, status: 'open', parkedAt: null, lines: [], lastAdd: null, customer: null }
      }

      const same = this.options.settings().mergeSameItem ? sale.lines.find((l) => l.itemId === item.id && l.unitCents === price.priceCents && !l.note) : undefined
      if (same) {
        const quantity = addQuantities(same.quantity, parsed.value)
        events.push(this.event('LineChanged', sale.id, { lineId: same.lineId, quantity, reason: 'same-item' }))
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

  /** CA 9: a quantidade nova da linha (pesável com 3 casas; por unidade, inteiro). */
  setQuantity(lineId: string, text: string): Promise<{ ok: true } | { ok: false; message: string }> {
    return this.run(async () => {
      const sale = this.current()
      const line = sale?.lines.find((l) => l.lineId === lineId)
      if (!sale || !line) return { ok: false, message: 'A linha não está mais na venda.' }
      const parsed = parseQuantity(text, line.fractional)
      if (!parsed.ok) return parsed
      if (parsed.value !== line.quantity) await this.commit([this.event('LineChanged', sale.id, { lineId, quantity: parsed.value, reason: 'quantity' })])
      return { ok: true }
    })
  }

  /** CA 11: observação da linha (sai no cupom). Vazia apaga. */
  setNote(lineId: string, note: string): Promise<void> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale?.lines.some((l) => l.lineId === lineId)) return
      const text = note.trim().slice(0, 140) || null
      await this.commit([this.event('LineChanged', sale.id, { lineId, note: text, reason: 'note' })])
    })
  }

  /** É o último lançamento, que sai sem supervisor (CA 10)? */
  isFreeRemoval(lineId: string): boolean {
    const sale = this.current()
    return !!sale && this.options.settings().freeRemoveLast && sale.lastAdd?.lineId === lineId
  }

  /**
   * CA 10: o último item lançado sai sem supervisor (se a linha somou o mesmo item, sai só o último lançamento); outra linha exige
   * `pos.cancel_item` (`authorized`). O PIN do supervisor é da PDV-04. Toda remoção fica registrada com quem estava no caixa.
   */
  removeLine(lineId: string, authorized: boolean): Promise<RemoveResult> {
    return this.run(async () => {
      const sale = this.current()
      const line = sale?.lines.find((l) => l.lineId === lineId)
      if (!sale || !line) return { kind: 'removed' }
      const operatorId = this.options.operatorId
      if (this.isFreeRemoval(lineId) && sale.lastAdd) {
        const rest = subtractQuantities(line.quantity, sale.lastAdd.quantity)
        await this.commit([(toMilli(rest) ?? 0) > 0
          ? this.event('LineChanged', sale.id, { lineId, quantity: rest, reason: 'remove-last', operatorId })
          : this.event('LineRemoved', sale.id, { lineId, reason: 'last-item', operatorId })])
        return { kind: 'removed' }
      }

      if (!authorized) return { kind: 'needs-authorization' }
      await this.commit([this.event('LineRemoved', sale.id, { lineId, reason: 'authorized', operatorId })])
      return { kind: 'removed' }
    })
  }

  /** CA 12: a venda vai para a lista de espera e a tela fica livre para a próxima. */
  park(): Promise<boolean> {
    return this.run(async () => {
      const sale = this.current()
      if (!sale || sale.lines.length === 0) return false
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
      return { number: await this.close(sale, reason) }
    })
  }

  private async close(sale: Sale, reason: string): Promise<string> {
    const text = reason.trim()
    if (!text) throw new Error('Informe o motivo do descarte.')
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

  /** O documento da venda decifrado, só para mostrar na tela (nunca para log). */
  async customerDocument(sale: Sale): Promise<string | null> {
    const id = sale.customer?.documentId
    if (!id || !this.options.cipher) return null
    const record = await this.options.store.getCustomerDocument(id)
    return record ? this.options.cipher.decrypt(record) : null
  }
}
