import { Banknote, CloudOff, CreditCard, QrCode, Wallet } from 'lucide-react'
import { type ReactNode, useMemo, useRef, useState } from 'react'
import { lookupStoreCredit, redeemStoreCredit } from '@/api/generated/bemaqui'
import type { StoreCreditView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorDica, FloorState } from '@/components/shopfloor/kit'
import { cn } from '@/lib/utils'
import type { CatalogPaymentMethod } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { type Cents, centsToDecimal, formatCents, toCents } from '@/pdv/money'
import { STORE_CREDIT_HINT } from '@/pdv/sale/return-model'
import {
  allowsChange, BRAND_LABEL, brandsOf, checkNsu, checkTenderNote, isCaptured, limitToRemaining, maxInstallmentsOf, methodAvailability, methodGroup,
  paidCents, parseMoney, quickCashOptions, remainingCents, salePaymentState, splitCash, type Tender,
} from '@/pdv/payment/payment-model'
import type { PaymentTerminal } from '@/pdv/payment/payment-terminal'
import { type Sale, saleTotal } from '@/pdv/sale/sale-model'
import type { CompletedSale, SaleRegister } from '@/pdv/sale/sale-register'
import { uuidv7 } from '@/pdv/uuid-v7'

const field = 'block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[24px] tabular-nums text-foreground'

/** Centavos para o campo de valor ("18,99"). */
const moneyText = (cents: Cents) => centsToDecimal(cents).replace('.', ',')

/** Instante da ação (fora da renderização). */
const nowIso = () => new Date().toISOString()

/** Troco acima disto (R$ 200) pede confirmação: protege do valor recebido digitado errado ("1000" no lugar de "10,00"). */
const HIGH_CHANGE_CENTS: Cents = 20_000

export const PIX_REMINDER ='Confira o recebimento no aplicativo do banco, não na tela do cliente.'

function MethodIcon({ method }: { method: CatalogPaymentMethod }) {
  const group = methodGroup(method)
  const Icon = group === 'cash' ? Banknote : group === 'pix' ? QrCode : group === 'card' ? CreditCard : Wallet
  return <Icon aria-hidden="true" className="size-6" />
}

function tenderLabel(t: Tender): string {
  const parts = [t.methodName]
  if (t.cardBrand) parts.push(BRAND_LABEL[t.cardBrand] ?? t.cardBrand)
  if (t.installments && t.installments > 1) parts.push(`${t.installments}x`)
  return parts.join(' · ')
}

/**
 * Tela de pagamento do caixa (PDV-03): total, "Falta", as formas da loja em botões grandes e os pagamentos já feitos. Fala só com a porta
 * `PaymentTerminal` (CA 11) e grava pelo `SaleRegister`, no aparelho: nunca espera a rede (CA 12).
 */
export function PaymentScreen({ sale, register, terminal, store, online, operatorId, onCompleted, cashBlocked = null, initialCreditCode = null }: {
  sale: Sale
  register: SaleRegister
  terminal: PaymentTerminal
  store: LocalStore
  online: boolean
  operatorId: string | null
  onCompleted: (done: CompletedSale) => void
  /** CX-01 CA 8: dinheiro na gaveta acima do teto da loja; a forma Dinheiro fica bloqueada com esta mensagem (as outras continuam). */
  cashBlocked?: string | null
  /** PDV-06 CA 9: o código do vale da troca, que já vem no campo do vale-troca. */
  initialCreditCode?: string | null
}) {
  const total = saleTotal(sale)
  const paid = paidCents(sale.tenders)
  const remaining = remainingCents(total, sale.tenders)
  const state = salePaymentState(sale.inPayment, total, sale.tenders)
  const methods = useMemo(() => [...store.list('paymentMethods')].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
    .map((m) => ({ method: m, availability: cashBlocked && methodGroup(m) === 'cash' ? 'cash-blocked' as const : methodAvailability(m, online) }))
    .filter((m) => m.availability !== 'hidden'), [store, online, cashBlocked])
  const usable = methods.filter((m) => m.availability === 'enabled')

  // PDV-06 CA 9: na troca, a forma Vale-troca já vem escolhida com o código do vale.
  const creditMethod = initialCreditCode ? usable.find((m) => methodGroup(m.method) === 'store-credit')?.method ?? null : null
  const [methodId, setMethodId] = useState<string | null>(() => creditMethod?.id ?? (usable.length === 1 ? usable[0].method.id : null))
  // PDV-06 CA 8: o código do vale, a consulta (saldo e validade) e a conferência em andamento.
  const [creditCode, setCreditCode] = useState(initialCreditCode ?? '')
  const [credit, setCredit] = useState<StoreCreditView | null>(null)
  const [checkingCredit, setCheckingCredit] = useState(false)
  const method = usable.find((m) => m.method.id === methodId)?.method ?? null
  const group = method ? methodGroup(method) : null
  const [value, setValue] = useState(() => moneyText(remaining))
  const [brand, setBrand] = useState<string | null>(null)
  const [installments, setInstallments] = useState(1)
  const [nsu, setNsu] = useState('')
  const [note, setNote] = useState('')
  const [message, setMessage] = useState<{ tone: 'error' | 'ok'; text: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const [terminalTender, setTerminalTender] = useState<Tender | null>(null)
  const valueRef = useRef<HTMLInputElement>(null)
  // Guarda síncrona contra o duplo envio (revisão de segurança): o `busy` do estado só muda na próxima renderização, e dois toques seguidos
  // chegariam antes dela. O ref fecha a porta no mesmo instante.
  const sending = useRef(false)
  // Troco acima de R$ 200 pede um segundo toque (revisão de segurança, item 6): guarda o valor recebido já conferido.
  const [highChangeAck, setHighChangeAck] = useState<Cents | null>(null)

  // Pagamento à espera de resposta ou sem resposta (integrações da PAG-02 a PAG-04; no manual não acontece): a tela própria vem do dado gravado.
  const open = sale.tenders.find((t) => t.status === 'Pending' || t.status === 'Unknown') ?? null
  const shown = open ?? terminalTender

  const choose = (m: CatalogPaymentMethod) => {
    setMethodId(m.id)
    setValue(moneyText(remaining))
    setBrand(null)
    setInstallments(1)
    setNsu('')
    setNote('')
    setMessage(null)
    setHighChangeAck(null)
    setCredit(null)
    requestAnimationFrame(() => { valueRef.current?.focus(); valueRef.current?.select() })
  }

  /** CA 8: consulta o vale no servidor (o código vai no corpo); o valor vem com o menor entre o saldo e o que falta. */
  const checkCredit = async () => {
    if (!creditCode.trim() || checkingCredit) return
    setCheckingCredit(true)
    setMessage(null)
    try {
      const found = await lookupStoreCredit({ code: creditCode.trim() })
      setCredit(found)
      if (found.usable) setValue(moneyText(Math.min(toCents(String(found.balance)), remaining)))
      else setMessage({ tone: 'error', text: found.problem ?? 'Este vale não pode ser usado.' })
    } catch (e) {
      setCredit(null)
      setMessage({ tone: 'error', text: e instanceof ApiError ? e.message : 'Não foi possível consultar o vale. Confira a conexão e tente de novo.' })
    } finally {
      setCheckingCredit(false)
    }
  }

  const cents = parseMoney(value)
  const cashChange = method && allowsChange(method) && cents !== null ? splitCash(cents, remaining).changeCents : 0

  /** CA 3: fora do dinheiro, o valor nunca passa do que falta. */
  const onValue = (text: string) => {
    const typed = parseMoney(text)
    if (method && !allowsChange(method) && typed !== null) {
      const limited = limitToRemaining(typed, remaining)
      if (limited.message) {
        setValue(moneyText(limited.valueCents))
        setMessage({ tone: 'error', text: limited.message })
        return
      }
    }
    setValue(text)
    setMessage(null)
  }

  const record = async (tender: Tender) => {
    const result = await register.recordTender(tender)
    if (result.kind === 'refused') { setMessage({ tone: 'error', text: result.message }); return }
    if (result.kind === 'completed') { onCompleted(result); return }
    if (tender.status === 'Captured') {
      setMethodId(null)
      setMessage({ tone: 'ok', text: `Recebido ${formatCents(tender.amountCents)} em ${tenderLabel(tender)}. Falta ${formatCents(result.remainingCents)}.` })
    }
  }

  const confirm = async () => {
    if (!method || busy || sending.current) return
    if (cents === null || cents <= 0) { setMessage({ tone: 'error', text: 'Informe o valor.' }); return }
    let amount = cents
    let received: Cents | null = null
    if (allowsChange(method)) {
      received = cents
      const split = splitCash(cents, remaining)
      amount = split.amountCents
      if (split.changeCents > HIGH_CHANGE_CENTS && highChangeAck !== cents) {
        setHighChangeAck(cents)
        setMessage({ tone: 'error', text: `Troco de ${formatCents(split.changeCents)}: confira o valor recebido. Se estiver certo, toque em Confirmar recebimento de novo.` })
        return
      }
    } else if (cents > remaining) {
      onValue(value)
      return
    }

    if (group === 'card' && !brand) { setMessage({ tone: 'error', text: 'Escolha a bandeira do cartão.' }); return }
    const checkedNsu = checkNsu(nsu)
    if (!checkedNsu.ok) { setMessage({ tone: 'error', text: checkedNsu.message }); return }
    const checkedNote = checkTenderNote(note)
    if (!checkedNote.ok) { setMessage({ tone: 'error', text: checkedNote.message }); return }

    if (group === 'store-credit' && (!credit?.usable || amount > toCents(String(credit.balance)))) {
      setMessage({ tone: 'error', text: credit?.usable ? 'O valor passa do saldo do vale.' : 'Consulte o vale antes de usar.' })
      return
    }

    sending.current = true
    setBusy(true)
    setMessage(null)
    const tenderId = uuidv7()
    try {
      // CA 8: o abatimento é feito no servidor antes de o pagamento entrar na venda (o mesmo id do pagamento: reenviar não abate de novo).
      if (group === 'store-credit') {
        try {
          await redeemStoreCredit({ code: creditCode.trim(), saleId: sale.id, tenderId, amount: Number(centsToDecimal(amount)) })
        } catch (e) {
          setMessage({ tone: 'error', text: e instanceof ApiError ? e.message : 'Não foi possível usar o vale. Confira a conexão e tente de novo.' })
          return
        }
      }

      const tender = await terminal.charge({
        tenderId, method, amountCents: amount, receivedCents: received, cardBrand: brand, installments, nsu: checkedNsu.value,
        note: checkedNote.value, operatorId, at: nowIso(),
      }, setTerminalTender)
      setTerminalTender(tender.status === 'Captured' ? null : tender)
      await record(tender)
    } catch {
      setMessage({ tone: 'error', text: 'Não foi possível gravar neste aparelho. Toque em Confirmar de novo; o que já foi recebido continua guardado.' })
    } finally {
      sending.current = false
      setBusy(false)
    }
  }

  const resolveUnknown = async (tender: Tender, approved: boolean) => {
    if (sending.current) return
    sending.current = true
    setBusy(true)
    try {
      const checked = await terminal.check(tender, approved, nowIso())
      setTerminalTender(checked.status === 'Captured' ? null : checked)
      await record(checked)
    } catch {
      setMessage({ tone: 'error', text: 'Não foi possível gravar neste aparelho. Tente de novo.' })
    } finally {
      sending.current = false
      setBusy(false)
    }
  }

  const back = async () => {
    const result = await register.cancelPayment()
    if (!result.ok) setMessage({ tone: 'error', text: result.message })
  }

  if (methods.length === 0) {
    return (
      <FloorState tone="error" title="Nenhuma forma de pagamento ativa nesta loja."
        message="Cadastre as formas em Financeiro › Formas de pagamento, no sistema. O caixa recebe a lista na próxima sincronização."
        action={<FloorButton tone="secondary" onClick={() => void back()} disabled={paid > 0}>Voltar aos itens</FloorButton>} />
    )
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_380px]">
      <section aria-label="Formas de pagamento" className="min-w-0 space-y-4">
        {shown ? <TerminalStatus tender={shown} busy={busy} onResolve={(approved) => void resolveUnknown(shown, approved)} onDismiss={() => setTerminalTender(null)} /> : null}

        {cashBlocked ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">{cashBlocked}</p> : null}
        <div role="group" aria-label="Escolha a forma" className="grid grid-cols-2 gap-[8px] sm:grid-cols-3">
          {methods.map(({ method: m, availability }) => (
            <FloorButton key={m.id} tone="secondary" aria-pressed={m.id === methodId} disabled={availability !== 'enabled' || busy || !!open}
              onClick={() => choose(m)} className={cn('min-h-[72px] flex-col gap-1', m.id === methodId && 'border-primary')}>
              <span className="flex items-center gap-2"><MethodIcon method={m} />{m.name}</span>
              {availability === 'needs-network' ? <span className="flex items-center gap-1 text-[14px] font-normal"><CloudOff aria-hidden="true" className="size-4" />Disponível com internet.</span> : null}
              {availability === 'cash-blocked' ? <span className="text-[14px] font-normal">Faça uma sangria.</span> : null}
            </FloorButton>
          ))}
        </div>

        {method && !open ? (
          <form className="space-y-4 rounded-md border border-border bg-card p-4" onSubmit={(e) => { e.preventDefault(); void confirm() }}>
            <label className="block space-y-2">
              <span className="font-semibold">{group === 'cash' ? 'Valor recebido' : 'Valor'}</span>
              <input ref={valueRef} inputMode="decimal" autoComplete="off" value={value} onChange={(e) => onValue(e.target.value)} className={field}
                aria-describedby="pay-message" />
            </label>

            {group === 'cash' ? (
              <div role="group" aria-label="Valores rápidos" className="flex flex-wrap gap-2">
                {quickCashOptions(remaining).map((c, i) => (
                  <FloorButton key={c} tone="secondary" onClick={() => { setValue(moneyText(c)); setMessage(null) }}>{i === 0 ? `Exato ${formatCents(c)}` : formatCents(c)}</FloorButton>
                ))}
              </div>
            ) : null}
            {group === 'cash' && cashChange > 0 ? (
              <p role="status" className="rounded-md bg-warn-soft p-4 text-warn-ink">
                <span className="block text-[18px] font-semibold">Troco</span>
                <span className="font-display text-[40px] font-bold tabular-nums">{formatCents(cashChange)}</span>
              </p>
            ) : null}

            {group === 'card' ? (
              <>
                <fieldset className="space-y-2">
                  <legend className="font-semibold">Bandeira</legend>
                  <div className="grid grid-cols-2 gap-[8px] sm:grid-cols-4">
                    {brandsOf(method).map((b) => (
                      <FloorButton key={b} tone="secondary" aria-pressed={brand === b} className={cn(brand === b && 'border-primary')} onClick={() => { setBrand(b); setMessage(null) }}>
                        {BRAND_LABEL[b] ?? b}
                      </FloorButton>
                    ))}
                  </div>
                </fieldset>
                {maxInstallmentsOf(method) > 1 ? (
                  <label className="block space-y-2">
                    <span className="font-semibold">Parcelas</span>
                    <select value={installments} onChange={(e) => setInstallments(Number(e.target.value))} className={field}>
                      {Array.from({ length: maxInstallmentsOf(method) }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n === 1 ? 'À vista' : `${n}x`}</option>)}
                    </select>
                  </label>
                ) : null}
                <div className="flex items-end gap-2">
                  <label className="block flex-1 space-y-2">
                    <span className="font-semibold">NSU (opcional)</span>
                    <input inputMode="text" autoComplete="off" maxLength={24} value={nsu} onChange={(e) => { setNsu(e.target.value); setMessage(null) }} className={field} />
                  </label>
                  <FloorDica nome="NSU" texto="É o número da transação impresso no comprovante da maquininha (até 12 letras ou números). Ajuda a achar o pagamento num estorno ou na conferência. Não é o número do cartão: nunca digite o número do cartão." />
                </div>
                <p className="text-muted-foreground">Cobre na maquininha e confirme aqui só depois de aprovado. A tela nunca pede o número do cartão.</p>
              </>
            ) : null}

            {group === 'store-credit' ? (
              <div className="space-y-2">
                <div className="flex items-end gap-2">
                  <label className="block flex-1 space-y-2">
                    <span className="font-semibold">Código do vale</span>
                    <input autoComplete="off" maxLength={24} value={creditCode} placeholder="VT-XXXX-XXXX-XXXX" className={field}
                      onChange={(e) => { setCreditCode(e.target.value); setCredit(null); setMessage(null) }} />
                  </label>
                  <FloorDica nome="Vale-troca" texto={STORE_CREDIT_HINT} />
                </div>
                <FloorButton tone="secondary" onClick={() => void checkCredit()} disabled={checkingCredit || !creditCode.trim()}>{checkingCredit ? 'Consultando…' : 'Consultar vale'}</FloorButton>
                {credit?.usable ? (
                  <p role="status" className="rounded-md bg-info-soft p-4 font-semibold text-info-ink tabular-nums">
                    Saldo {formatCents(toCents(String(credit.balance)))} · válido até {credit.expiresOn.split('-').reverse().join('/')}. O que sobrar continua no vale.
                  </p>
                ) : null}
              </div>
            ) : null}

            {group === 'pix' ? <p className="rounded-md bg-info-soft p-4 font-semibold text-info-ink">{PIX_REMINDER}</p> : null}

            {group !== 'cash' ? (
              <label className="block space-y-2">
                <span className="font-semibold">Observação (opcional)</span>
                <input autoComplete="off" maxLength={140} value={note} onChange={(e) => { setNote(e.target.value); setMessage(null) }} className={field} />
              </label>
            ) : null}

            <FloorButton type="submit" className="w-full" disabled={busy || (group === 'store-credit' && !credit?.usable)}>
              {group === 'pix' ? 'Recebi o Pix' : group === 'card' ? 'Cartão aprovado' : group === 'store-credit' ? 'Usar o vale' : 'Confirmar recebimento'}
            </FloorButton>
          </form>
        ) : !open ? <p className="text-[18px] text-muted-foreground">Escolha a forma de pagamento. Dá para pagar parte em cada forma.</p> : null}

        <p id="pay-message" role={message?.tone === 'error' ? 'alert' : 'status'} aria-live="polite"
          className={cn('min-h-[28px] text-[18px] font-semibold', message?.tone === 'error' ? 'text-crit-ink' : 'text-ok-ink')}>
          {message?.text ?? ''}
        </p>
      </section>

      <aside aria-label="Resumo do pagamento" className="flex min-w-0 flex-col gap-3">
        <section className="rounded-md border border-border bg-card p-4 tabular-nums">
          <p className="text-[16px] font-semibold text-muted-foreground">Total</p>
          <p className="font-display text-[32px] font-bold leading-tight">{formatCents(total)}</p>
          <p className="mt-3 text-[16px] font-semibold text-muted-foreground">Falta</p>
          <p aria-label={`Falta: ${formatCents(remaining)}`} className="font-display text-[44px] font-bold leading-tight">{formatCents(remaining)}</p>
          {paid > 0 ? <p className="mt-1 text-[16px] text-muted-foreground">Pago: {formatCents(paid)}</p> : null}
        </section>

        <section aria-label="Pagamentos feitos" className="rounded-md border border-border bg-card p-4">
          <h2 className="font-semibold">Pagamentos</h2>
          {sale.tenders.length === 0 ? <p className="text-muted-foreground">Nenhum pagamento ainda.</p> : (
            <ul className="mt-2 divide-y divide-border">
              {sale.tenders.map((t) => (
                <li key={t.tenderId} className="flex items-center justify-between gap-2 py-2 tabular-nums">
                  <span className="min-w-0">
                    <span className="block truncate font-semibold">{tenderLabel(t)}</span>
                    {isCaptured(t) && t.needsReconciliation ? <span className="block text-[14px] text-warn-ink">Conferir no extrato no fechamento</span> : null}
                    {!isCaptured(t) ? <span className="block text-[14px] text-crit-ink">{t.status === 'Failed' ? 'Não aprovado' : t.status === 'Unknown' ? 'Sem resposta' : 'Aguardando'}</span> : null}
                    {t.changeCents > 0 ? <span className="block text-[14px] text-muted-foreground">Troco {formatCents(t.changeCents)}</span> : null}
                  </span>
                  <b className={cn(!isCaptured(t) && 'text-muted-foreground line-through')}>{formatCents(t.amountCents)}</b>
                </li>
              ))}
            </ul>
          )}
        </section>

        <FloorButton tone="secondary" onClick={() => void back()} disabled={busy || state === 'PartiallyPaid'}>Voltar aos itens</FloorButton>
        {state === 'PartiallyPaid' ? (
          <p className="text-muted-foreground">Com pagamento recebido, a venda não volta aos itens: o pagamento só sai por estorno, com o supervisor. Receba o que falta.</p>
        ) : null}
      </aside>
    </div>
  )
}

/** CA 11: os estados `Pending`, `Failed` e `Unknown` têm tela (as integrações da PAG-02 a PAG-04 usam; o modo manual não passa por eles). */
function TerminalStatus({ tender, busy, onResolve, onDismiss }: { tender: Tender; busy: boolean; onResolve: (approved: boolean) => void; onDismiss: () => void }) {
  if (tender.status === 'Pending') {
    return (
      <div role="status" className="rounded-md border-2 border-info-soft bg-info-soft p-4 text-info-ink">
        <p className="text-[20px] font-semibold">Aguardando a maquininha…</p>
        <p>{formatCents(tender.amountCents)} em {tenderLabel(tender)}. Siga as instruções na maquininha.</p>
      </div>
    )
  }

  if (tender.status === 'Unknown') {
    return (
      <div role="alert" className="space-y-3 rounded-md border-2 border-warn-soft bg-warn-soft p-4 text-warn-ink">
        <p className="text-[20px] font-semibold">Não sabemos se o pagamento foi aprovado</p>
        <p>{formatCents(tender.amountCents)} em {tenderLabel(tender)}. Confira na maquininha (último comprovante) antes de cobrar de novo.</p>
        <div className="flex flex-wrap gap-[16px]">
          <FloorButton tone="secondary" disabled={busy} onClick={() => onResolve(false)}>Não foi aprovado</FloorButton>
          <FloorButton disabled={busy} onClick={() => onResolve(true)}>Foi aprovado</FloorButton>
        </div>
      </div>
    )
  }

  if (tender.status === 'Failed') {
    return (
      <div role="alert" className="space-y-3 rounded-md border-2 border-crit-soft bg-crit-soft p-4 text-crit-ink">
        <p className="text-[20px] font-semibold">Pagamento não aprovado</p>
        <p>{formatCents(tender.amountCents)} em {tenderLabel(tender)}. Tente de novo ou escolha outra forma.</p>
        <FloorButton tone="secondary" onClick={onDismiss}>Entendi</FloorButton>
      </div>
    )
  }

  return null
}

/** CA 6: venda concluída, troco em destaque, o cupom (IMP-01: `print`, pela porta `HardwareBridge`) e "Nova venda" (foco padrão). */
export function SaleCompletedView({ done, onNewSale, print = null }: { done: CompletedSale; onNewSale: () => void; print?: ReactNode }) {
  const pix = done.tenders.some((t) => isCaptured(t) && t.needsReconciliation)
  return (
    <section aria-label="Venda concluída" className="mx-auto max-w-xl space-y-4 rounded-md border border-border bg-card p-6 text-center">
      <h2 className="font-display text-[28px] font-bold">Venda concluída</h2>
      <p className="text-[18px] text-muted-foreground tabular-nums">Venda {done.number} · {formatCents(done.totalCents)}</p>
      {done.changeCents > 0 ? (
        <div role="status" className="rounded-md bg-warn-soft p-4 text-warn-ink">
          <p className="text-[20px] font-semibold">Troco</p>
          <p aria-label={`Troco: ${formatCents(done.changeCents)}`} className="font-display text-[56px] font-bold leading-tight tabular-nums">{formatCents(done.changeCents)}</p>
        </div>
      ) : <p className="text-[18px]">Sem troco.</p>}
      <ul className="space-y-1 text-left tabular-nums">
        {done.tenders.filter(isCaptured).map((t) => (
          <li key={t.tenderId} className="flex justify-between gap-2"><span className="truncate">{tenderLabel(t)}</span><b>{formatCents(t.amountCents)}</b></li>
        ))}
      </ul>
      {pix ? <p className="text-muted-foreground">O Pix conferido pelo operador aparece no fechamento do caixa para conferir contra o extrato.</p> : null}
      {print}
      <div className="flex flex-wrap justify-center gap-[16px]">
        <FloorButton autoFocus onClick={onNewSale}>Nova venda</FloorButton>
      </div>
    </section>
  )
}
