import { Link, useParams } from '@tanstack/react-router'
import { ArrowLeft, Copy, Share2 } from 'lucide-react'
import { useState } from 'react'
import { getGetSalesOrderQueryKey, useGetSalesOrder } from '@/api/generated/bemaqui'
import type { SalesOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useQueryClient } from '@tanstack/react-query'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { CancelOrderDialog, ChargeDialog, ConfirmOrderDialog, PaymentDialog, RefundDialog, ReopenDialog, SaleLink } from './OrderDialogs'
import { nextStepLabel, ORDER_DICAS, ORIGIN_LABEL, PROBLEMS, statusChip, timelineLabel } from './orders'

const STAGES = ['Rascunho', 'Aberto', 'Aguardando pagamento', 'Pago']
const STAGE_INDEX: Record<string, number> = { Draft: 0, Open: 1, AwaitingPayment: 2, Paid: 3, Cancelled: 0 }
const qty = (n: number) => n.toLocaleString('pt-BR', { maximumFractionDigits: 4 })
const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })

type DialogName = 'confirm' | 'charge' | 'pay' | 'cancel' | 'reopen' | 'refund' | null

function Items({ order }: { order: SalesOrderDetail }) {
  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full text-sm">
        <caption className="sr-only">Itens do pedido {order.number}</caption>
        <thead className="bg-muted text-left">
          <tr>
            <th className="p-2">Item</th><th className="p-2 text-right">Qtd.</th>
            <th className="p-2 text-right">Reservado<Dica nome="Reservado" texto={ORDER_DICAS.reservado} /></th>
            <th className="p-2 text-right">Preço</th><th className="p-2 text-right">Desconto</th><th className="p-2 text-right">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {order.lines.map((l) => (
            <tr key={l.id}>
              <td className="p-2">{l.description}</td>
              <td className="p-2 text-right tabular-nums">{qty(Number(l.quantity))} {l.unitCode}</td>
              <td className={Number(l.reserved) < Number(l.quantity) && (order.status === 'Open' || order.status === 'AwaitingPayment') ? 'p-2 text-right font-semibold text-crit-ink tabular-nums' : 'p-2 text-right tabular-nums'}>{qty(Number(l.reserved))}</td>
              <td className="p-2 text-right"><Money value={Number(l.unitPrice)} /></td>
              <td className="p-2 text-right">{Number(l.discount) ? <Money value={Number(l.discount)} /> : '—'}</td>
              <td className="p-2 text-right"><Money value={Number(l.total)} /></td>
            </tr>
          ))}
        </tbody>
        <tfoot className="border-t-2 border-border">
          {Number(order.orderDiscount) ? <tr><td className="p-2" colSpan={5}>Desconto do pedido</td><td className="p-2 text-right"><Money value={-Number(order.orderDiscount)} /></td></tr> : null}
          <tr className="font-semibold"><td className="p-2" colSpan={5}>Total</td><td className="p-2 text-right"><Money value={Number(order.total)} /></td></tr>
        </tfoot>
      </table>
    </div>
  )
}

const SLIP_STATUS: Record<string, string> = {
  Requested: 'Solicitado ao Inter', Issued: 'Emitido', Failed: 'Falhou', Paid: 'Pago', PaidPendingReview: 'Pagamento em conferência', Cancelled: 'Cancelado', Expired: 'Vencido',
}

function Charge({ order }: { order: SalesOrderDetail }) {
  const [copied, setCopied] = useState(false)
  const charge = order.charge
  if (!charge) return <p className="text-sm text-muted-foreground">Este pedido ainda não tem cobrança. {order.status === 'Open' ? 'Use "Gerar cobrança" no topo.' : ''}</p>
  const slip = charge.slip
  const copy = async () => {
    if (!slip?.pixCopyPaste) return
    await navigator.clipboard?.writeText(slip.pixCopyPaste)
    setCopied(true)
  }

  const share = async () => {
    if (!slip?.digitableLine) return
    const text = `Pedido ${order.number}: ${slip.digitableLine}${slip.pixCopyPaste ? `\nPix copia e cola: ${slip.pixCopyPaste}` : ''}`
    if (navigator.share) await navigator.share({ title: `Boleto do pedido ${order.number}`, text }).catch(() => undefined)
    else await navigator.clipboard?.writeText(text)
  }

  return (
    <div className="space-y-3 text-sm">
      <dl className="grid gap-2 rounded-md border border-border bg-card p-3 sm:grid-cols-2">
        <div><dt className="text-muted-foreground">Título</dt><dd className="tabular-nums">{charge.receivableNumber}</dd></div>
        <div><dt className="text-muted-foreground">Valor</dt><dd><Money value={Number(charge.amount)} /></dd></div>
        <div><dt className="text-muted-foreground">Situação do boleto</dt><dd>{slip ? <StatusChip tone={slip.status === 'Paid' ? 'ok' : slip.status === 'Failed' || slip.status === 'PaidPendingReview' ? 'warn' : 'info'}>{SLIP_STATUS[slip.status] ?? slip.status}</StatusChip> : 'Sem boleto'}</dd></div>
        <div><dt className="text-muted-foreground">Vencimento</dt><dd>{slip ? formatDate(slip.dueDate) : charge.dueDate ? formatDate(charge.dueDate) : '—'}</dd></div>
        {charge.refundedAt ? <div className="sm:col-span-2"><StatusChip tone="info">Valor devolvido</StatusChip> em {dateTime(charge.refundedAt)}</div> : null}
      </dl>
      {slip?.failureReason ? <p role="alert" className="text-crit-ink">{slip.failureReason}</p> : null}
      {slip?.digitableLine ? (
        <div className="space-y-2">
          <p><span className="block text-muted-foreground">Linha digitável</span><span className="break-all font-mono">{slip.digitableLine}</span></p>
          {slip.pixCopyPaste ? <p><span className="block text-muted-foreground">Pix copia e cola</span><span className="block break-all font-mono text-xs">{slip.pixCopyPaste}</span></p> : null}
          <div className="flex flex-wrap gap-2">
            {slip.pixCopyPaste ? <button type="button" onClick={() => void copy()} className="inline-flex min-h-11 items-center gap-1 rounded-md border border-border px-3 font-semibold"><Copy aria-hidden="true" className="size-4" />Copiar Pix</button> : null}
            <button type="button" onClick={() => void share()} className="inline-flex min-h-11 items-center gap-1 rounded-md border border-border px-3 font-semibold"><Share2 aria-hidden="true" className="size-4" />Compartilhar boleto</button>
            <span aria-live="polite" className="self-center text-ok-ink">{copied ? 'Copiado' : ''}</span>
          </div>
        </div>
      ) : slip?.status === 'Requested' ? <p className="text-muted-foreground">Boleto solicitado ao Inter. Assim que o banco confirmar, a linha digitável e o Pix aparecem aqui.</p> : null}
    </div>
  )
}

function Timeline({ order }: { order: SalesOrderDetail }) {
  return (
    <ol className="space-y-2" aria-label="Linha do tempo do pedido">
      {order.timeline.map((t, i) => (
        <li key={`${t.at}-${i}`} className="flex gap-3 rounded-md border border-border bg-card p-3 text-sm">
          <span className="w-24 flex-none text-xs text-muted-foreground tabular-nums">{dateTime(t.at)}</span>
          <span className="min-w-0 flex-1">
            <b className="block">{timelineLabel(t.action)}</b>
            <span className="text-muted-foreground">{t.actor}{t.reason ? ` · ${t.reason}` : ''}</span>
          </span>
          <StatusChip tone={statusChip(t.toStatus).tone}>{statusChip(t.toStatus).label}</StatusChip>
        </li>
      ))}
    </ol>
  )
}

/**
 * Pedido de venda (VND-02a): cabeçalho com o indicador de etapa e o próximo passo em destaque (a 400 px, fixo no rodapé), faixa de problema com a ação,
 * abas Itens, Cobrança, Linha do tempo e Trilha; link para a venda depois de pago. 404 "Pedido não encontrado." e conflito com "Recarregar".
 */
export function OrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetSalesOrder(id)
  const client = useQueryClient()
  const canCreate = useCan('sales.create')
  const canEdit = useCan('sales.edit')
  const canApprove = useCan('sales.approve')
  const [dialog, setDialog] = useState<DialogName>(null)
  const back = <Link to={'/app/loja/pedidos' as never} className="inline-flex items-center gap-1 text-sm font-semibold text-primary"><ArrowLeft aria-hidden="true" className="size-4" />Pedidos</Link>
  if (query.isPending) return <><PageHeading title="Pedido" />{back}<Skeleton className="mt-4 h-64" /></>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return (
      <>
        <PageHeading title="Pedido" />
        {back}
        <ErrorState title={notFound ? 'Pedido não encontrado.' : 'Não foi possível abrir o pedido.'} message={notFound ? 'Volte para pedidos e procure pelo número.' : 'Tente de novo em instantes.'}
          onRetry={notFound ? undefined : () => void query.refetch()} />
      </>
    )
  }

  const o = query.data
  const done = (next: SalesOrderDetail) => {
    client.setQueryData(getGetSalesOrderQueryKey(o.id), next)
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/sales/orders') })
    setDialog(null)
  }

  const chip = statusChip(o.status)
  const primary = o.status === 'Draft' && canCreate ? { verb: 'Confirmar pedido', dialog: 'confirm' as const }
    : o.status === 'Open' && canEdit ? { verb: 'Gerar cobrança', dialog: 'charge' as const }
      : (o.status === 'Open' || o.status === 'AwaitingPayment') && canApprove ? { verb: 'Registrar pagamento', dialog: 'pay' as const } : null
  const canCancel = (o.status === 'Paid' ? canApprove : canEdit) && o.status !== 'Cancelled'
  const props = { order: o, onDone: done, onOpenChange: (open: boolean) => { if (!open) setDialog(null) } }

  return (
    <div className="pb-20 sm:pb-0">
      <PageHeading title={`Pedido ${o.number}`} description={`${o.customerName} · ${ORIGIN_LABEL[o.origin] ?? o.origin}${o.sourceAppName ? ` (${o.sourceAppName})` : ''}`} />
      {back}
      <section className="my-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <div className="sm:col-span-2"><StageIndicator stages={STAGES} current={STAGE_INDEX[o.status] ?? 0} showLabel /></div>
        <div><span className="block text-muted-foreground">Situação</span><StatusChip tone={chip.tone}>{chip.label}</StatusChip></div>
        <div><span className="block text-muted-foreground">Total</span><span className="text-lg font-semibold"><Money value={Number(o.total)} /></span></div>
        {o.holdUntil && (o.status === 'Open' || o.status === 'AwaitingPayment') ? <div className="sm:col-span-2"><span className="text-muted-foreground">Reserva até </span><span className="tabular-nums">{dateTime(o.holdUntil)}</span><Dica nome="Prazo da reserva" texto={ORDER_DICAS.prazo} /></div> : null}
        {o.appLabel ? <div className="sm:col-span-2"><StatusChip tone="info">{o.appLabel}</StatusChip><Dica nome="Pedido de aplicativo" texto={ORDER_DICAS.conecta} /></div> : null}
        {o.saleId ? <div><SaleLink saleId={o.saleId} /></div> : null}
        {o.cancelReason ? <div className="sm:col-span-4"><StatusChip tone="neutral">Cancelado</StatusChip> {o.cancelReason}</div> : null}
      </section>
      {o.problems.map((p) => (
        <div key={p} role="status" className={p === 'PaidAfterCancel' || p === 'ReservationShort' ? 'mb-3 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink' : 'mb-3 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink'}>
          <b>{PROBLEMS[p]?.label ?? p}.</b> {PROBLEMS[p]?.text}
          {p === 'PaidAfterCancel' ? <Dica nome="Pago depois de cancelado" texto={ORDER_DICAS.pagoDepois} /> : null}
          {p === 'PaidAfterCancel' && canApprove ? (
            <span className="mt-2 flex flex-wrap gap-2">
              <button type="button" onClick={() => setDialog('reopen')} className="min-h-11 rounded-md bg-primary px-3 font-semibold text-primary-foreground">Reabrir pedido</button>
              <button type="button" onClick={() => setDialog('refund')} className="min-h-11 rounded-md border border-border bg-card px-3 font-semibold text-foreground">Devolver o valor</button>
            </span>
          ) : null}
        </div>
      ))}
      <div className="mb-4 flex flex-wrap gap-2">
        {primary ? (
          <button type="button" onClick={() => setDialog(primary.dialog)}
            className="fixed inset-x-4 bottom-20 z-20 min-h-12 rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground shadow sm:static sm:min-h-11">{primary.verb}</button>
        ) : o.status !== 'Cancelled' && nextStepLabel(o.nextStep) && o.nextStep === 'Pick' ? <p className="text-sm text-muted-foreground">Pago. A separação chega com a próxima versão dos pedidos.</p> : null}
        {o.status === 'Open' && canApprove && primary?.dialog !== 'pay' ? <button type="button" onClick={() => setDialog('pay')} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Registrar pagamento</button> : null}
        {canCancel ? <button type="button" onClick={() => setDialog('cancel')} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold text-crit-ink">{o.status === 'Paid' ? 'Cancelar pedido e estornar venda' : 'Cancelar pedido'}</button> : null}
      </div>
      <UrlTabs label="Seções do pedido" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <Items order={o} /> },
        { id: 'cobranca', label: 'Cobrança', content: <Charge order={o} /> },
        { id: 'linha-do-tempo', label: 'Linha do tempo', content: <Timeline order={o} /> },
        { id: 'trilha', label: 'Trilha', content: <RecordHistory entity="SalesOrder" id={o.id} /> },
      ]} />
      <ConfirmOrderDialog {...props} open={dialog === 'confirm'} />
      <ChargeDialog {...props} open={dialog === 'charge'} />
      <PaymentDialog {...props} open={dialog === 'pay'} />
      <CancelOrderDialog {...props} open={dialog === 'cancel'} />
      <ReopenDialog {...props} open={dialog === 'reopen'} />
      <RefundDialog {...props} open={dialog === 'refund'} />
    </div>
  )
}
