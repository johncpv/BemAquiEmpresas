import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetPaymentOrderQueryKey, useGetPaymentOrder } from '@/api/generated/bemaqui'
import type { PaymentResult } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DecisionActions } from './DecisionActions'
import { OrderItemsList } from './OrderItemsList'
import { OrderLevels } from './OrderLevels'
import { OrderReceipts } from './OrderReceipts'
import { PayDialog } from './PayDialog'
import { ORDER_DICAS, ORDER_STATUS } from './payment-orders'

const DECISION: Record<string, string> = { Approved: 'Aprovada', Rejected: 'Reprovada', Resubmitted: 'Reenviada' }

/** Ordem de pagamento (FIN-03a): títulos e destino do dinheiro, decisões, comprovantes e histórico; ações pela situação. */
export function PaymentOrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetPaymentOrder(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const canPay = useCan('finance.pay')
  const [next, setNext] = useState<{ label: string; onSelect: () => void }[]>([])
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a ordem." onRetry={() => query.refetch()} />
  const order = query.data
  const s = order.summary
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetPaymentOrderQueryKey(id) }) }
  const tab = (aba: string) => void navigate({ to: '.', search: { aba }, replace: true } as never)
  const paid = (result: PaymentResult) => setNext([
    { label: 'Anexar comprovante', onSelect: () => tab('comprovantes') },
    ...result.retentions.map((r) => ({
      label: `Gerar título do recolhimento (${r.payableNumber})`,
      onSelect: () => void navigate({ to: '/app/financeiro/pagar/novo', search: { descricao: `Recolhimento de retenções — ${r.payableNumber}`, valor: String(r.amount), vencimento: r.suggestedDueDate } } as never),
    })),
  ])
  const approved = () => setNext([{ label: 'Próxima OP aguardando você', onSelect: () => void navigate({ to: '/app/financeiro/ordens-de-pagamento', search: { filtro: 'aguardando-voce' } } as never) }])
  const decided = s.status !== 'Pending' && s.status !== 'Approved'

  return (
    <>
      <PageHeading title={`${s.number} — ${ORDER_STATUS[s.status]?.label ?? s.status}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={ORDER_STATUS[s.status]?.tone ?? 'neutral'}>{ORDER_STATUS[s.status]?.label ?? s.status}</StatusChip>
            <Money value={s.total} className="font-semibold text-foreground" /> · {s.items} {s.items === 1 ? 'título' : 'títulos'}
            {s.selfApproved ? <span className="inline-flex items-center"><Tag>Aprovada pelo próprio solicitante</Tag><Dica nome="Aprovada pelo próprio solicitante" texto={ORDER_DICAS.propria} /></span> : null}
          </p>
          <div className="flex flex-wrap gap-2">
            <DecisionActions order={order} refresh={refresh} onApproved={approved} />
            {s.status === 'Approved' && canPay ? <PayDialog order={order} refresh={refresh} onPaid={paid} /> : null}
          </div>
        </div>
      </PageHeading>
      {next.length ? <div className="mb-4"><NextSteps onDismiss={() => setNext([])} steps={next} /></div> : null}
      {decided ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Ordem {ORDER_STATUS[s.status]?.label.toLowerCase()}: só para consulta.{order.cancelReason ? ` Motivo: ${order.cancelReason}` : ''}{order.reversalReason ? ` Motivo do estorno: ${order.reversalReason}` : ''}</p> : null}
      <dl className="mb-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Solicitante</dt><dd>{s.requesterName ?? '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Enviada em</dt><dd className="tabular-nums">{formatDateTime(s.submittedAt)}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Data prevista</dt><dd className="tabular-nums">{formatDate(s.expectedDate)}</dd></div>
        <div><dt className="flex items-center text-xs text-muted-foreground">Alçada<Dica nome="Alçada" texto={ORDER_DICAS.alcada} /></dt><dd>{order.paidOn ? `Paga em ${formatDate(order.paidOn)}` : 'Um nível, quem aprova no Financeiro'}</dd></div>
      </dl>
      <UrlTabs label="Seções da ordem" fallback="titulos" tabs={[
        { id: 'titulos', label: 'Títulos', content: <OrderItemsList items={order.items} /> },
        { id: 'decisoes', label: 'Decisões', content: <><OrderLevels levels={order.levels ?? []} />{order.decisions.length === 0 ? <p className="text-sm text-muted-foreground">Aguardando a primeira decisão.</p> : (
          <ol className="space-y-2 text-sm">
            {order.decisions.map((d) => (
              <li key={d.decidedAt} className="rounded-md border border-border bg-card p-3">
                <b>{DECISION[d.decision] ?? d.decision}</b> por {d.decidedByName ?? '—'} em {formatDateTime(d.decidedAt)}{d.selfApproved ? ' (pelo próprio solicitante)' : ''}
                {d.comment ? <p className="mt-1 text-muted-foreground">{d.comment}</p> : null}
              </li>
            ))}
          </ol>
        )}</> },
        { id: 'comprovantes', label: 'Comprovantes', content: <OrderReceipts orderId={s.id} paid={s.status === 'Paid'} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="PaymentOrder" id={s.id} /> },
      ]} />
    </>
  )
}
