import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { approvePurchaseOrder, cancelPurchaseOrder, rejectPurchaseOrder, submitPurchaseOrder, useGetCashFlow, useListPurchaseOrderPayables } from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { daysUntil, todayIso } from '../purchasing'
import { ORDER_DICAS, projectedAfter } from './purchase-orders'
import { OrderLevels } from './OrderTabs'

interface Props {
  order: PurchaseOrderDetail
  set: (saved: PurchaseOrderDetail) => void
  refresh: () => Promise<unknown>
  onApproved: (last: boolean) => void
  onSubmitted: () => void
}

const button = (text: string, primary = false) => (
  <button type="button" className={primary ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>
)

function Reason({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="text-sm font-medium">{label}</label>
      <textarea id={id} value={value} maxLength={500} rows={3} onChange={(e) => onChange(e.target.value)} className={inputClass} />
    </div>
  )
}

/** Impacto da aprovação no caixa (CA 12): parcelas e datas estimadas e, com finance.view, o saldo projetado no dia de cada uma. */
function CashImpact({ order, enabled }: { order: PurchaseOrderDetail; enabled: boolean }) {
  const canFinance = useCan('finance.view')
  const last = order.installments.at(-1)?.estimatedDueDate
  const days = last ? Math.min(365, Math.max(1, daysUntil(last, todayIso()) + 1)) : 1
  const flow = useGetCashFlow({ view: 'daily', days }, { query: { enabled: enabled && canFinance && !!last } })
  const balanceOn = (date: string) => flow.data?.periods.find((p) => p.start <= date && date <= p.end)?.balance ?? null
  const rows = projectedAfter(order.installments, balanceOn)
  return (
    <div className="space-y-2">
      <p>Total do pedido: <Money value={order.total} className="font-semibold" /></p>
      {order.referenceAmount != null && order.referenceAmount !== order.total ? (
        <p className="inline-flex items-center">Valor de referência da alçada: <Money value={order.referenceAmount} className="ml-1 font-semibold" /><Dica nome="Valor de referência" texto={ORDER_DICAS.referencia} /></p>
      ) : null}
      <table className="w-full text-left text-sm">
        <caption className="sr-only">Parcelas previstas no fluxo de caixa</caption>
        <thead><tr className="text-xs text-muted-foreground"><th scope="col">Parcela</th><th scope="col">Data estimada</th><th scope="col" className="text-right">Valor</th>{canFinance ? <th scope="col" className="text-right">Saldo no dia</th> : null}</tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.number}>
              <td>{r.number}/{rows.length}</td>
              <td className="tabular-nums">{formatDate(r.estimatedDueDate)}</td>
              <td className="text-right"><Money value={r.amount} /></td>
              {canFinance ? <td className="text-right">{r.after === null ? '—' : <Money value={r.after} highlightNegative />}</td> : null}
            </tr>
          ))}
        </tbody>
      </table>
      <p className="text-xs text-muted-foreground">As parcelas entram no fluxo de caixa com a marca Estimado: a data é a previsão de entrega mais os dias de cada parcela.</p>
    </div>
  )
}

/**
 * Ações do pedido pela situação e pela permissão (CA 5, 9, 11, 12, 14, 16): enviar para aprovação, aprovar (diálogo grande com o impacto
 * no caixa, o fracionamento e o aviso "1 cotação"), reprovar com o motivo e cancelar com o impacto nos títulos previstos.
 */
export function OrderActions({ order, set, refresh, onApproved, onSubmitted }: Props) {
  const { acao } = useSearch({ strict: false }) as { acao?: string }
  const navigate = useNavigate()
  const canFinance = useCan('finance.view')
  const [comment, setComment] = useState('')
  const [reason, setReason] = useState('')
  const [approveOpen, setApproveOpen] = useState(acao === 'aprovar' && order.canDecide)
  const [cancelOpen, setCancelOpen] = useState(false)
  const payables = useListPurchaseOrderPayables(order.id, { query: { enabled: cancelOpen && canFinance } })
  const active = (payables.data ?? []).filter((p) => p.status !== 'Cancelled')
  const lastLevel = order.levels.filter((l) => l.status === 'Pending').length <= 1
  const clearAction = () => { if (acao) void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, acao: undefined }), replace: true } as never) }
  const v = order.version

  return (
    <div className="flex flex-wrap gap-2">
      {order.canSubmit ? (
        <ConfirmDialog verb="Enviar para aprovação" done="Pedido enviado para aprovação" title={`Enviar o ${order.number} para aprovação?`}
          description="A alçada é resolvida pelo valor de referência (o pedido e os do mesmo fornecedor em 30 dias). Os aprovadores do nível 1 recebem o aviso."
          impact={<p>Total: <Money value={order.total} className="font-semibold" /></p>}
          onConfirm={async () => { set(await submitPurchaseOrder(order.id, { version: v })); onSubmitted() }} onReload={refresh} trigger={button('Enviar para aprovação', true)} />
      ) : null}
      {order.canDecide ? (
        <>
          <ConfirmDialog size="lg" verb="Aprovar pedido" done={lastLevel ? 'Pedido aprovado. Previsão lançada no caixa.' : 'Pedido aprovado'} title={`Aprovar o ${order.number}?`}
            open={approveOpen} onOpenChange={(o) => { setApproveOpen(o); if (!o) clearAction() }} onOpen={() => setComment('')}
            description={`${order.partyName} · ${order.lines.length} ${order.lines.length === 1 ? 'item' : 'itens'}${lastLevel ? '' : ' · o pedido segue para o próximo nível'}`}
            impact={<CashImpact order={order} enabled={approveOpen} />}
            onConfirm={async () => { set(await approvePurchaseOrder(order.id, { comment: comment.trim() || null, version: v })); onApproved(lastLevel) }} onReload={refresh}
            trigger={button('Aprovar pedido', true)}>
            {order.splitWarning ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{order.splitWarning}</p> : null}
            {order.singleQuoteWarning ? <p role="note" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{order.singleQuoteWarning}</p> : null}
            <OrderLevels levels={order.levels} />
            <Reason id="comentario-aprovar" label="Comentário (opcional)" value={comment} onChange={setComment} />
          </ConfirmDialog>
          <ConfirmDialog verb="Reprovar pedido" done="Pedido reprovado" title={`Reprovar o ${order.number}?`} description="O pedido volta para rascunho e quem pediu recebe o motivo. As decisões desta tentativa ficam no histórico."
            onOpen={() => setComment('')} preconditions={[{ ok: comment.trim().length > 0, text: 'Motivo da reprovação escrito.' }]}
            onConfirm={async () => { set(await rejectPurchaseOrder(order.id, { comment: comment.trim(), version: v })) }} onReload={refresh} trigger={button('Reprovar pedido')}>
            <Reason id="comentario-reprovar" label="Motivo da reprovação" value={comment} onChange={setComment} />
          </ConfirmDialog>
        </>
      ) : null}
      {order.canEdit ? <button type="button" onClick={() => void navigate({ to: '/app/compras/pedidos/$id/editar', params: { id: order.id } } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Editar pedido</button> : null}
      {order.canCancel ? (
        <ConfirmDialog destructive size="lg" verb="Cancelar pedido" done="Pedido cancelado" title={`Cancelar o ${order.number}?`} open={cancelOpen} onOpenChange={setCancelOpen}
          description="As linhas de solicitação voltam para Abertas. Pedido cancelado não volta."
          impact={order.status === 'Draft' && order.attempt === 0
            ? <p>Nada muda no fluxo de caixa: o pedido ainda não foi aprovado.</p>
            : canFinance && payables.data
              ? <p>Cancela {active.length} {active.length === 1 ? 'título previsto' : 'títulos previstos'} de <Money value={active.reduce((s, p) => s + p.amount, 0)} className="font-semibold" /> no fluxo de caixa.</p>
              : <p>Cancela os títulos previstos do pedido no fluxo de caixa.</p>}
          onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
          onConfirm={async () => { set(await cancelPurchaseOrder(order.id, { reason: reason.trim(), version: v })) }} onReload={refresh} trigger={button('Cancelar pedido')}>
          <Reason id="motivo-cancelar" label="Motivo" value={reason} onChange={setReason} />
        </ConfirmDialog>
      ) : null}
    </div>
  )
}
