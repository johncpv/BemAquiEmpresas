import { Keyboard, LogOut, Pointer, Printer, Settings2 } from 'lucide-react'
import { type KeyboardEvent as ReactKeyboardEvent, useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import type { PosHandshake } from '@/api/generated/model'
import { FloorButton, FloorDica, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { cn } from '@/lib/utils'
import { loadDocumentKey } from '@/pdv/device-signing'
import type { CatalogItem } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { formatCents } from '@/pdv/money'
import { ManualTerminal, type PaymentTerminal } from '@/pdv/payment/payment-terminal'
import { beep } from '@/pdv/sale/beep'
import { applyPendingUpdate, markCashSessionOpen } from '@/pdv/app-update'
import { cashAlert, cashBlocked, closeCashSession, lastOpeningCents, loadCashSession, type LocalCashSession, openCashSession, openedByOtherMessage, recordSaleInSession } from '@/pdv/cash/cash-session'
import { CashArea } from '@/pages/pdv/cash/CashArea'
import { CashBusyScreen, CashOpenScreen } from '@/pages/pdv/cash/CashScreens'
import { parseCodeInput } from '@/pdv/sale/code-input'
import { aesDocumentCipher, formatDocument } from '@/pdv/sale/customer-document'
import { formatQuantity } from '@/pdv/sale/quantity'
import { currentSale, lineTotal, parkedSales, type SaleLine, saleTotal } from '@/pdv/sale/sale-model'
import { type AddResult, type CompletedSale, DEFAULT_SETTINGS, SaleRegister, SaleRuleError, type SaleSettings, SETTINGS_META } from '@/pdv/sale/sale-register'
import { ScanDetector } from '@/pdv/sale/scanner'
import { rememberReceipt, storedReceiptOf } from '@/pdv/print/print-jobs'
import { ReprintSheet, SalePrint } from '@/pages/pdv/print/ReceiptPrinting'
import { receiptHeader } from '@/pdv/print/use-printing'
import { PaymentScreen, SaleCompletedView } from './PaymentScreen'
import { CloseRegisterSheet, type CustomerHit, CustomerSheet, DiscardSheet, NoteSheet, ParkedSheet, ProductTile, QuantitySheet, RemoveSheet } from './sale-sheets'

/** "Venda {número} concluída ao reabrir o caixa", com o troco quando houver (PDV-03, revisão de segurança). */
function recoveredNotice(sales: readonly CompletedSale[]): { tone: 'ok'; text: string } | null {
  if (sales.length === 0) return null
  const text = sales.map((s) => `Venda ${s.number} concluída ao reabrir o caixa.${s.changeCents > 0 ? ` Troco: ${formatCents(s.changeCents)}.` : ''}`).join(' ')
  return { tone: 'ok', text }
}

export interface SaleOperator {
  id: string
  name: string
  /** Tem `pos.cancel_item` (remove qualquer linha sem supervisor). */
  canCancelItem: boolean
  /** Tem `pos.cash_out` (sangria, CX-01). */
  canCashOut: boolean
  /** Tem `pos.cash_in` (suprimento, CX-01). */
  canCashIn: boolean
}

type Sheet =
  | null
  | { kind: 'quantity'; lineId: string }
  | { kind: 'new-quantity'; item: CatalogItem; code: string | null }
  | { kind: 'note'; lineId: string }
  | { kind: 'remove'; lineId: string }
  | { kind: 'parked' }
  | { kind: 'discard'; saleId: string | null }
  | { kind: 'customer' }
  | { kind: 'close' }
  | { kind: 'settings' }
  | { kind: 'reprint' }

/** Teclas de função (CA 6), iguais nos dois modos. */
const FUNCTION_KEYS = [
  ['F2', 'Buscar'], ['F3', 'Quantidade'], ['F4', 'Observação'], ['F6', 'Remover item'],
  ['F7', 'Espera'], ['F8', 'Cliente'], ['F9', 'Descartar'], ['F10', 'Pagar'],
] as const
type FunctionKey = (typeof FUNCTION_KEYS)[number][0]
const F_KEYS = new Set<string>(FUNCTION_KEYS.map(([k]) => k))

const STORAGE_ERROR_TITLE = 'Não foi possível gravar neste aparelho.'
const STORAGE_ERROR_HINT = 'A última ação não foi gravada. Feche as outras janelas do caixa e toque em Tentar de novo; se continuar, reinicie o navegador. As vendas já gravadas não se perdem.'

function isField(el: EventTarget | null): el is HTMLInputElement | HTMLTextAreaElement {
  return el instanceof HTMLTextAreaElement || (el instanceof HTMLInputElement && !['checkbox', 'radio', 'button', 'submit'].includes(el.type))
}

/** Devolve o valor do campo (controlado pelo React) ao que era antes da leitura (CA 4). */
function restoreField(el: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(proto, 'value')?.set?.call(el, value)
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

const defaultMode = (uiMode: PosHandshake['uiMode']): SaleSettings['mode'] => (uiMode === 'Keyboard' ? 'keyboard' : 'touch')

/**
 * Tela de venda do caixa (PDV-02): itens à esquerda, total grande à direita, barra de funções. Tudo sai do `LocalStore` (catálogo em memória e
 * diário da venda): a tela nunca espera a rede; a única coisa que precisa de internet é a busca do cliente cadastrado (CA 15).
 * - **Teclado/leitor** (CA 6): o foco fica no campo de código; `3*código` define a quantidade; F2 a F10 abrem as funções.
 * - **Toque** (CA 7): busca pelo nome e grade de produtos com botões grandes.
 * - O leitor em modo teclado é reconhecido em qualquer campo pelo tempo entre as teclas (CA 4).
 * - **Pagar** (F10, PDV-03) leva à tela de pagamento, que só fala com a porta `PaymentTerminal` (padrão: `ManualTerminal`, maquininha externa).
 */
export function PdvSale({ store, handshake, operator, online, searchCustomers, onLeave, terminal }: {
  store: LocalStore
  handshake: PosHandshake
  operator: SaleOperator
  online: boolean
  searchCustomers: ((term: string) => Promise<CustomerHit[]>) | null
  onLeave: () => void
  terminal?: PaymentTerminal
}) {
  const [cash, setCash] = useState<LocalCashSession | null | undefined>(undefined)
  const [lastOpening, setLastOpening] = useState(0)
  // A venda concluída leva o id do turno (a OFF-02 e a CX-03 juntam as vendas da sessão).
  const cashIdRef = useRef<string | null>(null)
  useEffect(() => { if (cash !== undefined) cashIdRef.current = cash?.id ?? null }, [cash])
  // A troca de versão do app espera o turno fechar (OFF-01 CA 3): o service worker lê esta marca.
  useEffect(() => { if (cash !== undefined) markCashSessionOpen(!!cash) }, [cash])
  const [settings, setSettings] = useState<SaleSettings>({ mode: defaultMode(handshake.uiMode), ...DEFAULT_SETTINGS })
  const settingsRef = useRef(settings)
  useEffect(() => { settingsRef.current = settings }, [settings])
  const [register, setRegister] = useState<SaleRegister | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let alive = true
    void (async () => {
      try {
        const [saved, loaded, opening] = await Promise.all([store.getMeta<Partial<SaleSettings>>(SETTINGS_META), loadCashSession(store), lastOpeningCents(store)])
        if (!alive) return
        if (saved) setSettings((s) => ({ ...s, ...saved }))
        setLastOpening(opening ?? 0)
        cashIdRef.current = loaded?.id ?? null
        const opened = await SaleRegister.open({
          store, operatorId: operator.id, salePrefix: handshake.salePrefix, settings: () => ({ ...settingsRef.current, freeRemoveLast: settingsRef.current.freeRemoveLast && (store.catalogState()?.storeSettings?.freeRemoveLast ?? true) }),
          cipher: aesDocumentCipher(loadDocumentKey), cashSessionId: () => cashIdRef.current,
        })
        // Venda concluída ao reabrir (queda entre o último pagamento e o fechamento): conta no turno como as outras.
        let session = loaded
        for (const done of loaded ? opened.recovered : []) session = await recordSaleInSession(store, done.tenders) ?? session
        if (!alive) return
        setCash(session)
        setRegister(opened)
      } catch {
        if (alive) setFailure(STORAGE_ERROR_TITLE)
      }
    })()
    return () => { alive = false }
  }, [store, operator.id, handshake.salePrefix, attempt])

  const saveSettings = async (next: SaleSettings) => {
    setSettings(next)
    try { await store.setMeta(SETTINGS_META, next) } catch { setFailure(STORAGE_ERROR_TITLE) }
  }

  if (failure && !register) {
    return <FloorState tone="error" title={failure} message={STORAGE_ERROR_HINT} action={<FloorButton onClick={() => { setFailure(null); setAttempt((a) => a + 1) }}>Tentar de novo</FloorButton>} />
  }

  if (!register || cash === undefined) return <FloorSkeleton rows={3} />

  // CX-01 CA 1: sem turno aberto, o operador abre com o fundo de troco (padrão: o da última abertura); sem turno não se vende (PDV-02 CA 17).
  if (!cash) {
    return (
      <CashOpenScreen operatorName={operator.name} defaultCents={lastOpening} onLeave={onLeave}
        onOpen={async (openingCents) => {
          const opened = await openCashSession(store, { operator, openingCents, registerId: handshake.registerId, deviceEpoch: handshake.epoch })
          setLastOpening(openingCents)
          setCash(opened)
        }} />
    )
  }

  // CX-01 CA 2: uma sessão por caixa; outro operador não vende no turno de quem abriu.
  const busy = openedByOtherMessage(cash, operator.id)
  if (busy) return <CashBusyScreen message={busy} onLeave={onLeave} />

  return (
    <SaleScreen store={store} register={register} operator={operator} online={online} settings={settings} onSettings={saveSettings} terminal={terminal}
      registerId={handshake.registerId} storeName={handshake.storeName}
      searchCustomers={searchCustomers} onLeave={onLeave} cash={cash} onCash={setCash} place={`${handshake.storeName} · Caixa ${String(handshake.registerNumber).padStart(2, '0')}`}
      onCloseRegister={async () => { await closeCashSession(store, operator); setCash(null); applyPendingUpdate() }} />
  )
}

function SaleScreen({ store, register, operator, online, settings, onSettings, searchCustomers, onLeave, onCloseRegister, terminal: givenTerminal, cash, onCash, place, registerId, storeName }: {
  store: LocalStore
  registerId: string
  storeName: string
  register: SaleRegister
  operator: SaleOperator
  online: boolean
  settings: SaleSettings
  onSettings: (next: SaleSettings) => Promise<void>
  searchCustomers: ((term: string) => Promise<CustomerHit[]>) | null
  onLeave: () => void
  onCloseRegister: () => Promise<void>
  terminal?: PaymentTerminal
  cash: LocalCashSession
  onCash: (session: LocalCashSession) => void
  place: string
}) {
  const terminal = useMemo(() => givenTerminal ?? new ManualTerminal(), [givenTerminal])
  const [completed, setCompleted] = useState<CompletedSale | null>(null)
  // CX-01: painel do caixa (CA 10), aviso de sangria (CA 7) e teto do dinheiro (CA 8). Os limites vêm da loja, no catálogo do caixa.
  const [cashView, setCashView] = useState<null | { startWith: 'CashOut' | 'CashIn' | null }>(null)
  const [cashNotice, setCashNotice] = useState<string | null>(null)
  const book = useSyncExternalStore(register.subscribe, register.getSnapshot)
  const sale = currentSale(book)
  const parked = parkedSales(book)
  const lines = useMemo(() => sale?.lines ?? [], [sale])
  const [code, setCode] = useState('')
  // Venda que fechou ao reabrir (a queda foi entre o último pagamento e o fechamento): avisa o número e o troco a devolver.
  const [notice, setNotice] = useState<{ tone: 'error' | 'ok'; text: string } | null>(() => recoveredNotice(register.recovered))
  const [storageError, setStorageError] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [suggestion, setSuggestion] = useState(0)
  const [sheet, setSheet] = useState<Sheet>(null)
  const [decrypted, setDecrypted] = useState<{ id: string; text: string } | null>(null)
  const codeRef = useRef<HTMLInputElement>(null)
  const keyboard = settings.mode === 'keyboard'
  const selected = lines.find((l) => l.lineId === selectedId) ?? lines.at(-1) ?? null
  const total = saleTotal(sale)
  const paying = !!sale?.inPayment

  // O cliente da venda para a tela: o documento é decifrado só para mostrar (nunca vai para log).
  const documentId = sale?.customer?.documentId ?? null
  useEffect(() => {
    let alive = true
    if (sale && documentId) {
      const kind = sale.customer?.documentKind ?? 'cpf'
      void register.customerDocument(sale).then((text) => { if (alive && text) setDecrypted({ id: documentId, text: formatDocument(text, kind) }) }).catch(() => { /* só não mostra */ })
    }
    return () => { alive = false }
  }, [register, sale, documentId])
  const customerView = { name: sale?.customer?.customerName ?? null, document: documentId && decrypted?.id === documentId ? decrypted.text : null }

  const focusCode = useCallback(() => {
    if (settings.mode === 'keyboard') requestAnimationFrame(() => codeRef.current?.focus())
  }, [settings.mode])

  const closeSheet = useCallback(() => { setSheet(null); focusCode() }, [focusCode])

  /** Toda ação passa por aqui: erro de gravação vira o aviso de armazenamento (a tela não finge que gravou). */
  const guard = useCallback(async <T,>(action: () => Promise<T>): Promise<T | undefined> => {
    try {
      const result = await action()
      setStorageError(false)
      return result
    } catch (e) {
      if (e instanceof SaleRuleError) throw e
      setStorageError(true)
      return undefined
    }
  }, [])

  const handleAdd = useCallback((result: AddResult | undefined) => {
    if (!result) return
    switch (result.kind) {
      case 'added':
        setCode('')
        setSelectedId(result.line.lineId)
        setNotice(null)
        break
      case 'not-found':
        beep()
        setNotice({ tone: 'error', text: `Produto não encontrado: ${result.code}` })
        requestAnimationFrame(() => codeRef.current?.select())
        break
      case 'no-price':
        beep()
        setNotice({ tone: 'error', text: `${result.item.name} está sem preço na tabela da loja.` })
        break
      case 'needs-quantity':
        setCode('')
        setSheet({ kind: 'new-quantity', item: result.item, code: result.code })
        break
      case 'invalid-quantity':
        beep()
        setNotice({ tone: 'error', text: result.message })
        break
      case 'search':
        setNotice({ tone: 'error', text: `Nenhum produto com “${result.text}”.` })
        break
      case 'locked':
        beep()
        setNotice({ tone: 'error', text: result.message })
        break
    }
  }, [])

  const scan = useCallback(async (value: string) => handleAdd(await guard(() => register.addFromInput(value))), [guard, handleAdd, register])
  const pick = useCallback(async (item: CatalogItem, quantity: string | null) => {
    handleAdd(await guard(() => register.addItem(item, quantity)))
    focusCode()
  }, [focusCode, guard, handleAdd, register])

  const parsedCode = parseCodeInput(code)
  const suggestions = useMemo(() => (!parsedCode.isCode && parsedCode.text.length >= 3 ? store.searchItems(parsedCode.text, 8) : []), [store, parsedCode.isCode, parsedCode.text])

  const runKey = useCallback((key: FunctionKey) => {
    setNotice(null)
    switch (key) {
      case 'F2': codeRef.current?.focus(); codeRef.current?.select(); break
      case 'F3': if (selected) setSheet({ kind: 'quantity', lineId: selected.lineId }); break
      case 'F4': if (selected) setSheet({ kind: 'note', lineId: selected.lineId }); break
      case 'F6': if (selected) setSheet({ kind: 'remove', lineId: selected.lineId }); break
      case 'F7':
        if (lines.length) void guard(() => register.park()).then((ok) => { if (ok) setNotice({ tone: 'ok', text: 'Venda em espera. Pode começar a próxima.' }) })
        else setSheet({ kind: 'parked' })
        break
      case 'F8': if (sale) setSheet({ kind: 'customer' }); else setNotice({ tone: 'error', text: 'Lance um item antes de informar o cliente.' }); break
      case 'F9': if (sale && lines.length) setSheet({ kind: 'discard', saleId: null }); break
      case 'F10':
        if (lines.length) {
          setCompleted(null)
          void guard(() => register.startPayment()).then((r) => { if (r && !r.ok) setNotice({ tone: 'error', text: r.message }) })
        }
        break
    }
  }, [guard, lines.length, register, sale, selected])

  // Leitor em qualquer campo (CA 4) e teclas de função (CA 6).
  const handlers = useRef({ scan, runKey, sheetOpen: false, blocked: false })
  useEffect(() => { handlers.current = { scan, runKey, sheetOpen: sheet !== null, blocked: paying || completed !== null || cashView !== null } }, [scan, runKey, sheet, paying, completed, cashView])
  useEffect(() => {
    const detector = new ScanDetector()
    let snapshot: { el: HTMLInputElement | HTMLTextAreaElement; value: string } | null = null
    const onKey = (e: KeyboardEvent) => {
      if (e.target === codeRef.current) {
        detector.reset()
      } else {
        const step = detector.push(e.key, e.timeStamp)
        if (step.kind === 'start') snapshot = isField(e.target) ? { el: e.target, value: e.target.value } : null
        if (step.kind === 'scan') {
          e.preventDefault()
          e.stopPropagation()
          if (snapshot) restoreField(snapshot.el, snapshot.value)
          snapshot = null
          // No pagamento (PDV-03) a venda não muda de itens: a leitura só avisa.
          if (handlers.current.blocked) { beep(); return }
          void handlers.current.scan(step.code)
          return
        }
      }

      if (!handlers.current.sheetOpen && !handlers.current.blocked && F_KEYS.has(e.key)) {
        e.preventDefault()
        handlers.current.runKey(e.key as FunctionKey)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [])

  // Modo Teclado: o foco volta sempre para o campo de código.
  useEffect(() => { if (keyboard && !sheet) focusCode() }, [keyboard, sheet, focusCode])

  const onCodeKey = (e: ReactKeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const step = e.key === 'ArrowDown' ? 1 : -1
      if (suggestions.length) { setSuggestion((i) => (i + step + suggestions.length) % suggestions.length); return }
      const index = lines.findIndex((l) => l.lineId === selected?.lineId)
      const next = lines[Math.min(Math.max(index + step, 0), lines.length - 1)]
      if (next) setSelectedId(next.lineId)
      return
    }

    if (e.key !== 'Enter') return
    e.preventDefault()
    if (suggestions.length) {
      const item = suggestions[Math.min(suggestion, suggestions.length - 1)]
      setCode('')
      void pick(item, parsedCode.quantity)
      return
    }

    if (code.trim()) void scan(code)
  }

  const sheetLine = sheet && 'lineId' in sheet ? lines.find((l) => l.lineId === sheet.lineId) ?? null : null
  const discardTarget = sheet?.kind === 'discard' ? (sheet.saleId ? book.sales.find((s) => s.id === sheet.saleId) ?? null : sale) : null
  const pendingSales = parked.length + (lines.length ? 1 : 0)

  const limits = store.catalogState()?.cashLimits ?? null
  const storeSettings = store.catalogState()?.storeSettings ?? null
  const header = receiptHeader(store, storeName)
  const decryptDocument = useCallback((id: string) => register.decryptDocument(id), [register])
  const completeSale = async (done: CompletedSale) => {
    // IMP-01: a venda fica guardada para o cupom e a 2ª via antes de a tela oferecer a impressão (o CPF continua só no registro cifrado).
    const receipt = storedReceiptOf(done, operator.name)
    if (receipt) await rememberReceipt(store, receipt).catch(() => { /* sem o registro, a tela só não imprime esta venda */ })
    setCompleted(done)
    void recordSaleInSession(store, done.tenders).then((s) => { if (s) { onCash(s); setCashNotice(cashAlert(s, limits)) } }).catch(() => { /* o painel recompõe no próximo turno */ })
  }

  // IMP-01 CA 10 e 13: sem documento fiscal para a venda (o modo "só cupom não fiscal" não foi aceito, ou o texto do aceite mudou e falta o aceite
  // novo, e a NFC-e ainda não existe), a loja não vende.
  if (storeSettings && !storeSettings.canSell && !paying && !completed) {
    const newTerms = storeSettings.fiscalMode === 'NonFiscal'
    return (
      <FloorState title={newTerms ? 'Esta loja parou de vender.' : 'Esta loja ainda não pode vender.'}
        message={newTerms
          ? 'O texto do aceite do modo “só cupom não fiscal” mudou. O Proprietário lê e aceita o texto novo na retaguarda, em Loja → Lojas e caixas → Documento fiscal, e o caixa libera na próxima sincronização.'
          : 'Falta escolher o documento fiscal da loja. O Proprietário liga o modo “só cupom não fiscal” na retaguarda, em Loja → Lojas e caixas → Documento fiscal, e o caixa libera na próxima sincronização.'}
        action={<FloorButton tone="secondary" onClick={onLeave}>Sair do caixa</FloorButton>} />
    )
  }

  if (cashView) {
    return (
      <CashArea store={store} session={cash} operator={operator} place={place} startWith={cashView.startWith} onSession={(s) => { onCash(s); setCashNotice(null) }}
        onBack={() => { setCashView(null); focusCode() }} onClose={() => { setCashView(null); setSheet({ kind: 'close' }) }} />
    )
  }

  const alert = cashNotice ? (
    <div role="status" className="flex flex-wrap items-center justify-between gap-[8px] rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">
      <span>{cashNotice}</span>
      <FloorButton tone="secondary" onClick={() => { setCompleted(null); setCashView({ startWith: 'CashOut' }) }}>Fazer sangria</FloorButton>
    </div>
  ) : null

  if (completed || (sale && paying)) {
    return (
      <div className="space-y-3">
        <p className="truncate text-[18px]"><span className="text-muted-foreground">No caixa:</span> <b>{operator.name}</b> · {completed ? 'Venda concluída' : 'Pagamento'}</p>
        {completed ? alert : null}
        {completed ? <SaleCompletedView done={completed} onNewSale={() => { setCompleted(null); setSelectedId(null); setNotice(null); focusCode() }}
          print={completed.saleId ? (
            <SalePrint store={store} saleId={completed.saleId} header={header} operatorId={operator.id} registerId={registerId}
              mode={storeSettings?.receiptPrintMode ?? 'Ask'} decryptDocument={decryptDocument} />
          ) : null} /> : null}
        {!completed && sale ? (
          <PaymentScreen sale={sale} register={register} terminal={terminal} store={store} online={online} operatorId={operator.id} onCompleted={(done) => void completeSale(done)} cashBlocked={cashBlocked(cash, limits)} />
        ) : null}
      </div>
    )
  }

  const linesList = (
    <section aria-label="Itens da venda" className="min-h-0 flex-1 overflow-y-auto rounded-md border border-border bg-card">
      {lines.length === 0 ? (
        <p className="p-6 text-center text-[18px] text-muted-foreground">Bipe ou busque um produto para começar.</p>
      ) : (
        <ol className="divide-y divide-border">
          {lines.map((line, i) => <LineRow key={line.lineId} line={line} index={i} selected={line.lineId === selected?.lineId} onSelect={() => { setSelectedId(line.lineId); focusCode() }} />)}
        </ol>
      )}
    </section>
  )

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <p className="min-w-0 flex-1 truncate text-[18px]"><span className="text-muted-foreground">No caixa:</span> <b>{operator.name}</b></p>
        <FloorButton tone="secondary" onClick={() => setSheet({ kind: 'parked' })} aria-label={`Vendas em espera: ${parked.length}`}>
          Em espera <span className="tabular-nums">({parked.length})</span>
        </FloorButton>
        <FloorDica nome="Venda em espera" texto="O cliente esqueceu algo? Deixe a venda em espera (F7), atenda o próximo e retome depois. Ela fica guardada neste caixa, mesmo sem internet." />
        <FloorButton tone="secondary" onClick={() => setSheet({ kind: 'settings' })} aria-label="Ajustes do caixa"><Settings2 aria-hidden="true" className="size-6" /></FloorButton>
        <FloorButton tone="secondary" onClick={() => setSheet({ kind: 'reprint' })} aria-label="Reimprimir cupom"><Printer aria-hidden="true" className="size-6" /><span className="max-sm:sr-only">Reimprimir</span></FloorButton>
        <FloorButton tone="secondary" onClick={() => setCashView({ startWith: null })}>Caixa</FloorButton>
        <FloorButton tone="secondary" onClick={() => setSheet({ kind: 'close' })}>Fechar caixa</FloorButton>
        <FloorButton tone="secondary" onClick={onLeave} aria-label="Sair do caixa"><LogOut aria-hidden="true" className="size-6" /><span className="max-sm:sr-only">Sair</span></FloorButton>
      </div>

      {alert}

      {storageError ? (
        <div role="alert" className="space-y-1 rounded-md border border-crit-soft bg-crit-soft p-4 text-crit-ink">
          <p className="text-[18px] font-semibold">{STORAGE_ERROR_TITLE}</p>
          <p>{STORAGE_ERROR_HINT}</p>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_380px]">
        <div className="flex min-w-0 flex-col gap-3 lg:h-[calc(100dvh-220px)]">
          <div className="relative">
            <label htmlFor="pdv-code" className="mb-1 block font-semibold">{keyboard ? 'Código, quantidade (3*código) ou nome' : 'Buscar produto pelo nome'}</label>
            <input id="pdv-code" ref={codeRef} value={code} autoComplete="off" spellCheck={false} inputMode={keyboard ? 'text' : 'search'}
              onChange={(e) => { setCode(e.target.value); setSuggestion(0); setNotice(null) }} onKeyDown={onCodeKey}
              onBlur={() => { if (keyboard && !handlers.current.sheetOpen) setTimeout(() => { if (!handlers.current.sheetOpen && document.activeElement === document.body) codeRef.current?.focus() }, 0) }}
              placeholder={keyboard ? 'Bipe o produto ou digite o código' : 'Digite 3 letras do nome'}
              aria-describedby="pdv-notice" className="block min-h-[64px] w-full rounded-md border-2 border-input bg-card px-4 text-[24px] tabular-nums text-foreground" />
            {keyboard && suggestions.length ? (
              <ul role="listbox" aria-label="Produtos encontrados" className="absolute inset-x-0 top-full z-20 mt-1 overflow-hidden rounded-md border border-border bg-popover shadow-lg">
                {suggestions.map((item, i) => {
                  const price = store.priceOf(item.id)
                  return (
                    <li key={item.id} role="option" aria-selected={i === suggestion}
                      onMouseDown={(e) => { e.preventDefault(); setCode(''); void pick(item, parsedCode.quantity) }}
                      className={cn('flex min-h-[56px] cursor-pointer items-center justify-between gap-3 px-4 text-[18px]', i === suggestion && 'bg-info-soft text-info-ink')}>
                      <span className="truncate">{item.name}</span>
                      <b className="tabular-nums">{price ? `${formatCents(price.priceCents)}/${item.unit.toLowerCase()}` : 'sem preço'}</b>
                    </li>
                  )
                })}
              </ul>
            ) : null}
            <p id="pdv-notice" role={notice?.tone === 'error' ? 'alert' : 'status'} aria-live="polite"
              className={cn('mt-1 min-h-[28px] text-[18px] font-semibold', notice?.tone === 'error' ? 'text-crit-ink' : 'text-ok-ink')}>
              {notice?.text ?? ''}
            </p>
          </div>
          {keyboard ? linesList : <ProductGrid store={store} term={parsedCode.isCode ? '' : parsedCode.text} onPick={(item) => { setCode(''); void pick(item, null) }} />}
        </div>

        <aside aria-label="Total e funções" className="flex min-w-0 flex-col gap-3 lg:h-[calc(100dvh-220px)]">
          {keyboard ? null : linesList}
          <section className="rounded-md border border-border bg-card p-4">
            <p className="text-[16px] font-semibold text-muted-foreground">Total a pagar</p>
            <p aria-live="polite" aria-label={`Total a pagar: ${formatCents(total)}`} className="font-display text-[44px] font-bold leading-tight tabular-nums">{formatCents(total)}</p>
            <dl className="mt-2 grid grid-cols-2 gap-x-2 text-[16px] tabular-nums">
              <dt className="text-muted-foreground">Itens</dt><dd className="text-right">{lines.length}</dd>
              <dt className="text-muted-foreground">Cliente</dt><dd className="truncate text-right">{customerView.name ?? customerView.document ?? '—'}</dd>
            </dl>
          </section>
          <div role="group" aria-label="Funções" className="grid grid-cols-2 gap-[8px]">
            {FUNCTION_KEYS.map(([key, label]) => (
              <FloorButton key={key} tone={key === 'F10' ? 'primary' : 'secondary'} className={cn('justify-start text-[17px]', key === 'F10' && 'col-span-2')}
                aria-keyshortcuts={key} onClick={() => runKey(key)}>
                <kbd className="rounded border border-border px-1.5 text-[14px] font-semibold">{key}</kbd>{key === 'F7' && !lines.length ? 'Vendas em espera' : key === 'F7' ? 'Deixar em espera' : label}
              </FloorButton>
            ))}
          </div>
        </aside>
      </div>

      {sheet?.kind === 'quantity' || sheet?.kind === 'new-quantity' ? <QuantitySheet open
        title={sheet?.kind === 'new-quantity' ? sheet.item.name : sheetLine ? `Quantidade · ${sheetLine.name}` : 'Quantidade'}
        fractional={sheet?.kind === 'new-quantity' ? sheet.item.fractional : sheetLine?.fractional ?? false}
        initial={sheet?.kind === 'quantity' && sheetLine ? formatQuantity(sheetLine.quantity, false) : ''}
        onCancel={closeSheet}
        onSave={async (value) => {
          if (sheet?.kind === 'new-quantity') {
            const result = await guard(() => register.addItem(sheet.item, value, sheet.code))
            closeSheet()
            handleAdd(result)
            return null
          }

          if (sheet?.kind !== 'quantity') return null
          const result = await guard(() => register.setQuantity(sheet.lineId, value, operator.canCancelItem))
          if (result && !result.ok) return result.message
          closeSheet()
          return null
        }} /> : null}
      {sheet?.kind === 'note' ? <NoteSheet open line={sheetLine} onCancel={closeSheet}
        onSave={async (note) => { if (sheet?.kind === 'note') await guard(() => register.setNote(sheet.lineId, note)); closeSheet() }} /> : null}
      <RemoveSheet open={sheet?.kind === 'remove'} line={sheetLine} free={!!sheetLine && register.isFreeRemoval(sheetLine.lineId)} canCancelItem={operator.canCancelItem}
        onCancel={closeSheet}
        onConfirm={async () => {
          if (sheet?.kind !== 'remove') return
          const result = await guard(() => register.removeLine(sheet.lineId, operator.canCancelItem))
          if (result?.kind === 'removed') { setSelectedId(null); closeSheet() }
        }} />
      <ReprintSheet open={sheet?.kind === 'reprint'} store={store} header={header} operatorId={operator.id} registerId={registerId} decryptDocument={decryptDocument} onClose={closeSheet} />
      <ParkedSheet open={sheet?.kind === 'parked'} sales={parked} onClose={closeSheet}
        onResume={async (id) => { await guard(() => register.resume(id)); setSelectedId(null); closeSheet() }}
        onDiscard={(id) => setSheet({ kind: 'discard', saleId: id })} />
      {sheet?.kind === 'discard' ? <DiscardSheet open sale={discardTarget} onCancel={closeSheet}
        onConfirm={async (reason) => {
          if (sheet?.kind !== 'discard') return null
          try {
            const done = await guard(() => register.discard(reason, sheet.saleId ?? undefined))
            if (done) setNotice({ tone: 'ok', text: `Venda ${done.number} descartada.` })
            setSelectedId(null)
            closeSheet()
            return null
          } catch (e) {
            return e instanceof Error ? e.message : 'Não foi possível descartar.'
          }
        }} /> : null}
      {sheet?.kind === 'customer' ? <CustomerSheet open online={online} current={customerView} searchCustomers={searchCustomers} onCancel={closeSheet}
        onSave={async (input) => {
          const result = await guard(() => register.setCustomer(input))
          if (result && !result.ok) return result.message
          closeSheet()
          return null
        }} /> : null}
      <CloseRegisterSheet open={sheet?.kind === 'close'} pending={pendingSales} onCancel={closeSheet}
        onReview={() => setSheet({ kind: 'parked' })}
        onConfirm={async () => { await guard(onCloseRegister); setSheet(null) }} />
      <SettingsSheet open={sheet?.kind === 'settings'} settings={settings} onClose={closeSheet} onChange={(next) => void onSettings(next)} />
    </div>
  )
}

function LineRow({ line, index, selected, onSelect }: { line: SaleLine; index: number; selected: boolean; onSelect: () => void }) {
  return (
    <li>
      <button type="button" onClick={onSelect} aria-current={selected ? 'true' : undefined}
        className={cn('grid min-h-[56px] w-full grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-3 px-3 py-2 text-left tabular-nums', selected && 'bg-info-soft text-info-ink')}>
        <span className="text-[14px] text-muted-foreground">{String(index + 1).padStart(3, '0')}</span>
        <span className="min-w-0">
          <span className="block truncate text-[18px] font-semibold">{line.name}</span>
          <span className="block text-[15px] text-muted-foreground">{formatQuantity(line.quantity, line.fractional)} {line.unit.toLowerCase()} × {formatCents(line.unitCents)}</span>
          {line.note ? <span className="block truncate text-[15px] italic">Obs.: {line.note}</span> : null}
        </span>
        <span className="text-[18px] font-bold">{formatCents(lineTotal(line))}</span>
      </button>
    </li>
  )
}

const GRID_LIMIT = 48

/** Modo Toque (CA 7 e 8): com 3 letras ou mais, os resultados da busca local; sem busca, os produtos da loja em ordem de nome. */
function ProductGrid({ store, term, onPick }: { store: LocalStore; term: string; onPick: (item: CatalogItem) => void }) {
  const all = useMemo(() => [...store.list('items')].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR')).slice(0, GRID_LIMIT), [store])
  const searching = term.trim().length >= 3
  const items = useMemo(() => (searching ? store.searchItems(term, GRID_LIMIT) : all), [all, searching, store, term])
  return (
    <section aria-label={searching ? 'Produtos encontrados' : 'Produtos'} className="min-h-0 flex-1 overflow-y-auto">
      {items.length === 0 ? <p className="p-6 text-center text-[18px] text-muted-foreground">{searching ? `Nenhum produto com “${term.trim()}”.` : 'Nenhum produto disponível nesta loja.'}</p> : (
        <div className="grid grid-cols-2 gap-[8px] sm:grid-cols-3 xl:grid-cols-4">
          {items.map((item) => <ProductTile key={item.id} item={item} priceCents={store.priceOf(item.id)?.priceCents ?? null} onPick={() => onPick(item)} />)}
        </div>
      )}
    </section>
  )
}

/** Ajustes deste caixa: modo da tela (CA 6 e 7) e o mesmo item em uma linha ou em linhas separadas (CA 5). */
function SettingsSheet({ open, settings, onClose, onChange }: { open: boolean; settings: SaleSettings; onClose: () => void; onChange: (next: SaleSettings) => void }) {
  const option = (active: boolean) => cn('flex-1 justify-start', active && 'border-primary')
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onClose() }} title="Ajustes deste caixa"
      actions={<FloorButton className="w-full" onClick={onClose}>Pronto</FloorButton>}>
      <fieldset className="space-y-2">
        <legend className="font-semibold">Modo da tela</legend>
        <div className="flex flex-wrap gap-2">
          <FloorButton tone="secondary" aria-pressed={settings.mode === 'keyboard'} className={option(settings.mode === 'keyboard')} onClick={() => onChange({ ...settings, mode: 'keyboard' })}>
            <Keyboard aria-hidden="true" className="size-6" />Teclado e leitor
          </FloorButton>
          <FloorButton tone="secondary" aria-pressed={settings.mode === 'touch'} className={option(settings.mode === 'touch')} onClick={() => onChange({ ...settings, mode: 'touch' })}>
            <Pointer aria-hidden="true" className="size-6" />Toque
          </FloorButton>
        </div>
      </fieldset>
      <fieldset className="space-y-2">
        <legend className="font-semibold">Mesmo produto lançado de novo</legend>
        <div className="flex flex-wrap gap-2">
          <FloorButton tone="secondary" aria-pressed={settings.mergeSameItem} className={option(settings.mergeSameItem)} onClick={() => onChange({ ...settings, mergeSameItem: true })}>Soma na mesma linha</FloorButton>
          <FloorButton tone="secondary" aria-pressed={!settings.mergeSameItem} className={option(!settings.mergeSameItem)} onClick={() => onChange({ ...settings, mergeSameItem: false })}>Uma linha por lançamento</FloorButton>
        </div>
      </fieldset>
    </FloorSheet>
  )
}
