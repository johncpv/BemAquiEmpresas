import { CloudOff, Printer, Undo2 } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { approveSaleReturn, lookupSaleForReturn, requestSaleReturn } from '@/api/generated/bemaqui'
import type { ReturnCandidate, SaleReturnView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorDica, FloorState } from '@/components/shopfloor/kit'
import { cn } from '@/lib/utils'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { formatCents, toCents } from '@/pdv/money'
import { NEEDS_NETWORK } from '@/pdv/offline/offline-features'
import { checkNsu } from '@/pdv/payment/payment-model'
import { printDocument, targetOf } from '@/pdv/print/print-jobs'
import { usePrinting } from '@/pdv/print/use-printing'
import { buildReturnReceipt, REFUND_LABEL } from '@/pdv/sale/return-receipt'
import { lookupBody, RETURN_NOT_FOUND, STORE_CREDIT_HINT } from '@/pdv/sale/return-model'
import { ReasonPicker, type SupervisorApi, type SupervisorApproval, SupervisorDialog, type SupervisorRequest } from './supervisor'

type Refund = 'StoreCredit' | 'Cash' | 'CardReversal'
type Destination = 'Restock' | 'Damaged'
interface Choice { quantity: number; destination: Destination; reason: string }

const field = 'block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[20px] text-foreground'
const money = (value: number | string) => formatCents(toCents(String(value)))
const qty = (value: number) => String(value).replace('.', ',')

/**
 * Devolução no caixa (PDV-06): localizar a venda pelo cupom ou CPF → escolher itens e quantidades → destino e motivo → reembolso → concluir. Só com
 * rede (a venda original é conferida no servidor). Dinheiro e venda fora do prazo chamam o supervisor (PIN no caixa, PDV-04). Sai o recibo e, no
 * vale, o vale-troca impresso. Carregando, vazio ("Nenhuma venda encontrada com este número ou CPF.") e erro.
 */
export function ReturnArea({ store, online, place, operatorName, supervisor, onBack, onExchange }: {
  store: LocalStore
  online: boolean
  place: string
  operatorName: string
  supervisor: SupervisorApi | null
  onBack: () => void
  /** Troca (CA 9): volta à venda com o código do vale para pagar a venda nova. */
  onExchange: (code: string) => void
}) {
  const [term, setTerm] = useState('')
  const [searching, setSearching] = useState(false)
  const [found, setFound] = useState<readonly ReturnCandidate[] | null>(null)
  const [sale, setSale] = useState<ReturnCandidate | null>(null)
  const [choices, setChoices] = useState<Record<string, Choice>>({})
  const [refund, setRefund] = useState<Refund>('StoreCredit')
  const [exchange, setExchange] = useState(false)
  const [cardPaymentId, setCardPaymentId] = useState<string | null>(null)
  const [nsu, setNsu] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [asking, setAsking] = useState<{ request: SupervisorRequest; returnId: string } | null>(null)
  const [done, setDone] = useState<SaleReturnView | null>(null)
  const [printed, setPrinted] = useState<'idle' | 'ok' | 'failed'>('idle')
  const { bridge, printers } = usePrinting(store)

  if (!online) {
    return (
      <FloorState title="Devolução" message={`${NEEDS_NETWORK} A venda original é conferida no servidor.`}
        action={<FloorButton tone="secondary" onClick={onBack}><CloudOff aria-hidden="true" className="size-5" />Voltar à venda</FloorButton>} />
    )
  }

  const search = async (e: FormEvent) => {
    e.preventDefault()
    const body = lookupBody(term)
    if (!body) { setError('Digite o número do cupom ou o CPF do cliente.'); return }
    setSearching(true)
    setError(null)
    try {
      const result = await lookupSaleForReturn(body)
      setFound(result.sales)
      if (result.sales.length === 1) pick(result.sales[0])
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível buscar a venda. Confira a conexão e tente de novo.')
    } finally {
      setSearching(false)
    }
  }

  const pick = (candidate: ReturnCandidate) => {
    setSale(candidate)
    setChoices({})
    setRefund('StoreCredit')
    setExchange(false)
    setCardPaymentId(candidate.cardPayments.find((p) => Number(p.reversible) > 0)?.paymentId ?? null)
    setNsu('')
    setError(null)
  }

  const chosen = sale ? sale.lines.filter((l) => (choices[l.saleLineId]?.quantity ?? 0) > 0) : []
  const estimate = chosen.reduce((sum, l) => sum + Math.round(Number(l.unitAmount) * 100 * choices[l.saleLineId].quantity), 0)
  const setChoice = (lineId: string, patch: Partial<Choice>) =>
    setChoices((c) => ({ ...c, [lineId]: { ...(c[lineId] ?? { quantity: 0, destination: 'Restock' as Destination, reason: '' }), ...patch } }))

  const finish = async (returnId: string, approval: SupervisorApproval | null) => {
    const view = await approveSaleReturn(returnId, { supervisorApprovalId: approval?.approvalId ?? null })
    setAsking(null)
    setDone(view)
    void print(view)
  }

  const conclude = async () => {
    if (!sale || busy) return
    if (chosen.length === 0) { setError('Escolha pelo menos um item e a quantidade que volta.'); return }
    const missing = chosen.find((l) => (choices[l.saleLineId].reason ?? '').trim().length < 3)
    if (missing) { setError(`Informe o motivo de "${missing.description}".`); return }
    if (refund === 'CardReversal') {
      if (!cardPaymentId) { setError('Escolha o pagamento no cartão.'); return }
      const checked = checkNsu(nsu)
      if (!checked.ok || !checked.value) { setError(checked.ok ? 'Estorne na maquininha e informe o NSU do estorno.' : checked.message); return }
    }

    setBusy(true)
    setError(null)
    try {
      const requested = await requestSaleReturn({
        saleId: sale.saleId, refundKind: refund, exchange: refund === 'StoreCredit' && exchange,
        cardPaymentId: refund === 'CardReversal' ? cardPaymentId : null, cardNsu: refund === 'CardReversal' ? nsu.trim() : null,
        lines: chosen.map((l) => ({ saleLineId: l.saleLineId, quantity: choices[l.saleLineId].quantity, destination: choices[l.saleLineId].destination, reason: choices[l.saleLineId].reason.trim() })),
      })
      // O prazo pode vencer entre o pedido e a conclusão: o servidor pede o supervisor (supervisor_required) e o caixa chama.
      let needsSupervisor = requested.needsSupervisor
      if (!needsSupervisor) {
        try {
          await finish(requested.id, null)
          return
        } catch (err) {
          if (!(err instanceof ApiError) || err.code !== 'supervisor_required') throw err
          needsSupervisor = true
        }
      }
      if (needsSupervisor) {
        const cents = toCents(String(requested.total))
        setAsking({
          returnId: requested.id,
          request: {
            action: 'Return', title: requested.lateMessage ?? (refund === 'Cash' ? 'Devolução em dinheiro' : 'Devolução fora do prazo'), saleId: requested.id, lineId: null,
            beforeCents: cents, afterCents: 0,
            summary: `Devolução ${requested.number} da venda ${sale.number}: ${formatCents(cents)} em ${REFUND_LABEL[refund].toLowerCase()}.`,
            reason: chosen.map((l) => choices[l.saleLineId].reason.trim()).join('; ').slice(0, 140),
          },
        })
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível concluir a devolução. Confira a conexão e tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const print = async (view: SaleReturnView) => {
    try {
      const outcome = await printDocument(bridge, buildReturnReceipt(view, place, operatorName), targetOf(printers[0]))
      setPrinted(outcome.ok ? 'ok' : 'failed')
    } catch {
      setPrinted('failed')
    }
  }

  const reset = () => { setDone(null); setSale(null); setFound(null); setTerm(''); setChoices({}); setPrinted('idle') }

  if (done) {
    const code = done.storeCredit?.code ?? null
    return (
      <section aria-label="Devolução concluída" className="space-y-4 rounded-md border border-border bg-card p-5">
        <h2 className="text-[24px] font-bold">Devolução {done.number} concluída</h2>
        <p className="text-[20px] tabular-nums">Devolvido: <b>{money(done.total)}</b> em {REFUND_LABEL[done.refundKind].toLowerCase()}.</p>
        {code ? (
          <p className="rounded-md bg-info-soft p-4 text-info-ink">
            <span className="block text-[18px] font-semibold">Vale-troca</span>
            <span className="font-display text-[32px] font-bold tabular-nums">{code}</span>
            <span className="block">Válido até {done.storeCredit?.expiresOn.split('-').reverse().join('/')}. Entregue o vale impresso ao cliente.</span>
          </p>
        ) : null}
        {done.refundKind === 'Cash' ? <p className="text-[18px] font-semibold">Entregue {money(done.total)} ao cliente: o dinheiro já saiu do caixa.</p> : null}
        <p role="status" className={cn('text-[18px] font-semibold', printed === 'failed' ? 'text-crit-ink' : 'text-ok-ink')}>
          {printed === 'ok' ? 'Recibo enviado para a impressora.' : printed === 'failed' ? 'Não foi possível imprimir. Confira a impressora e toque em Imprimir de novo.' : ''}
        </p>
        <div className="flex flex-wrap gap-[8px]">
          {done.isExchange && code ? <FloorButton onClick={() => onExchange(code)}>Venda nova com o vale</FloorButton> : null}
          <FloorButton tone="secondary" onClick={() => void print(done)}><Printer aria-hidden="true" className="size-5" />Imprimir de novo</FloorButton>
          <FloorButton tone="secondary" onClick={reset}>Nova devolução</FloorButton>
          <FloorButton tone="secondary" onClick={onBack}>Voltar à venda</FloorButton>
        </div>
      </section>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="flex min-w-0 flex-1 items-center gap-2 text-[22px] font-bold"><Undo2 aria-hidden="true" className="size-6" />Devolução</h2>
        <FloorButton tone="secondary" onClick={onBack}>Voltar à venda</FloorButton>
      </div>

      <form onSubmit={(e) => void search(e)} className="flex flex-wrap items-end gap-2 rounded-md border border-border bg-card p-4">
        <label className="block min-w-[240px] flex-1 space-y-2">
          <span className="font-semibold">Número do cupom ou CPF do cliente</span>
          <input value={term} onChange={(e) => setTerm(e.target.value)} autoComplete="off" className={field} placeholder="LOJA-02.3-000127 ou 000.000.000-00" />
        </label>
        <FloorButton type="submit" disabled={searching}>{searching ? 'Buscando…' : 'Buscar'}</FloorButton>
      </form>

      {searching ? <p aria-busy="true" className="text-[18px]">Buscando a venda…</p> : null}
      {found && found.length === 0 ? <FloorState title={RETURN_NOT_FOUND} message="Confira o número no cupom. Venda feita sem CPF só é achada pelo número." /> : null}
      {found && found.length > 1 && !sale ? (
        <ul aria-label="Vendas encontradas" className="divide-y divide-border rounded-md border border-border bg-card">
          {found.map((s) => (
            <li key={s.saleId}>
              <button type="button" onClick={() => pick(s)} className="flex min-h-[56px] w-full items-center justify-between gap-2 px-4 text-left text-[18px] hover:bg-muted">
                <span className="tabular-nums"><b>{s.number}</b> · {s.businessDate.split('-').reverse().join('/')}</span><b className="tabular-nums">{money(s.total)}</b>
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {sale ? (
        <section aria-label="Itens da venda" className="space-y-4 rounded-md border border-border bg-card p-4">
          <p className="text-[18px]"><b className="tabular-nums">{sale.number}</b> · {sale.businessDate.split('-').reverse().join('/')} · <span className="tabular-nums">{money(sale.total)}</span></p>
          {sale.late ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">{sale.lateMessage} Só segue com o supervisor.</p> : null}
          <ul className="divide-y divide-border">
            {sale.lines.map((l) => {
              const c = choices[l.saleLineId]
              const q = c?.quantity ?? 0
              const max = Number(l.returnable)
              return (
                <li key={l.saleLineId} className="space-y-2 py-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="min-w-0 flex-1 text-[18px]"><b>{l.description}</b><span className="block text-[15px] text-muted-foreground tabular-nums">Vendido {qty(Number(l.sold))} · já devolvido {qty(Number(l.returned))} · {money(l.unitAmount)} cada</span></span>
                    <FloorButton tone="secondary" aria-label={`Menos ${l.description}`} disabled={q <= 0} onClick={() => setChoice(l.saleLineId, { quantity: Math.max(0, q - 1) })}>−</FloorButton>
                    <span aria-label={`Quantidade que volta de ${l.description}`} className="min-w-[48px] text-center text-[22px] font-bold tabular-nums">{qty(q)}</span>
                    <FloorButton tone="secondary" aria-label={`Mais ${l.description}`} disabled={q + 1 > max} onClick={() => setChoice(l.saleLineId, { quantity: Math.min(max, q + 1) })}>+</FloorButton>
                  </div>
                  {max === 0 ? <p className="text-muted-foreground">Nada mais deste item pode voltar.</p> : null}
                  {q > 0 ? (
                    <div className="space-y-2">
                      <div role="group" aria-label={`Destino de ${l.description}`} className="flex flex-wrap gap-[8px]">
                        {(['Restock', 'Damaged'] as const).map((d) => (
                          <FloorButton key={d} tone={(c?.destination ?? 'Restock') === d ? 'primary' : 'secondary'} aria-pressed={(c?.destination ?? 'Restock') === d}
                            onClick={() => setChoice(l.saleLineId, { destination: d })}>{d === 'Restock' ? 'Volta ao estoque' : 'Avariado (perda)'}</FloorButton>
                        ))}
                      </div>
                      <ReasonPicker action="Return" value={c?.reason ?? ''} onChange={(reason) => setChoice(l.saleLineId, { reason })} />
                    </div>
                  ) : null}
                </li>
              )
            })}
          </ul>

          <fieldset className="space-y-2">
            <legend className="flex items-center font-semibold">Reembolso<FloorDica nome="Vale-troca" texto={STORE_CREDIT_HINT} /></legend>
            <div className="flex flex-wrap gap-[8px]">
              {(['StoreCredit', 'Cash', 'CardReversal'] as const).map((r) => (
                <FloorButton key={r} tone={refund === r ? 'primary' : 'secondary'} aria-pressed={refund === r}
                  disabled={r === 'CardReversal' && !sale.cardPayments.some((p) => Number(p.reversible) > 0)} onClick={() => { setRefund(r); setError(null) }}>
                  {REFUND_LABEL[r]}
                </FloorButton>
              ))}
            </div>
            {refund === 'StoreCredit' ? (
              <label className="flex min-h-[56px] items-center gap-3 text-[18px]">
                <input type="checkbox" className="size-6" checked={exchange} onChange={(e) => setExchange(e.target.checked)} />
                Troca: o cliente já vai levar outro produto (o vale paga a venda nova)
              </label>
            ) : null}
            {refund === 'Cash' ? <p className="text-muted-foreground">Devolução em dinheiro pede o supervisor e não pode passar do dinheiro que há no caixa.</p> : null}
            {refund === 'CardReversal' ? (
              <div className="space-y-2">
                <p className="text-muted-foreground">Estorne na maquininha primeiro e informe o NSU do estorno (está no comprovante).</p>
                <div role="group" aria-label="Pagamento no cartão" className="flex flex-wrap gap-[8px]">
                  {sale.cardPayments.filter((p) => Number(p.reversible) > 0).map((p) => (
                    <FloorButton key={p.paymentId} tone={cardPaymentId === p.paymentId ? 'primary' : 'secondary'} aria-pressed={cardPaymentId === p.paymentId}
                      onClick={() => setCardPaymentId(p.paymentId)}>{p.cardBrand ?? 'Cartão'} · até {money(p.reversible)}</FloorButton>
                  ))}
                </div>
                <label className="block space-y-2">
                  <span className="font-semibold">NSU do estorno</span>
                  <input inputMode="text" autoComplete="off" maxLength={24} value={nsu} onChange={(e) => setNsu(e.target.value)} className={field} />
                </label>
              </div>
            ) : null}
          </fieldset>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-[20px] tabular-nums">Valor a devolver: <b>{formatCents(estimate)}</b></p>
            <FloorButton onClick={() => void conclude()} disabled={busy || chosen.length === 0}>{busy ? 'Concluindo…' : 'Concluir devolução'}</FloorButton>
          </div>
        </section>
      ) : null}

      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}

      <SupervisorDialog request={asking?.request ?? null} api={supervisor} online={online}
        onApproved={async (approval) => {
          if (!asking) return
          try {
            await finish(asking.returnId, approval)
          } catch (err) {
            setAsking(null)
            setError(err instanceof ApiError ? err.message : 'Não foi possível concluir a devolução. Tente de novo.')
          }
        }}
        onCancel={() => setAsking(null)} />
    </div>
  )
}
