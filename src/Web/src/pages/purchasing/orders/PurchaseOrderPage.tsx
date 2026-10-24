import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetPurchaseOrderQueryKey, useGetPurchaseOrder } from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate, formatDateTime } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { OrderActions } from './OrderActions'
import { OrderDecisions, OrderDocuments, OrderHistory, OrderItems, OrderLevels, OrderPayables, OrderReceipts } from './OrderTabs'
import { closedLabel, lateLabel, ORDER_DICAS, ORDER_STATUS, orderDestination } from './purchase-orders'

/** Faixa do estado em modo leitura (padrão de tela da Fase 3). */
function readOnlyBand(o: PurchaseOrderDetail): string | null {
  if (o.status === 'Cancelled') return `Pedido cancelado${o.cancelledByName ? ` por ${o.cancelledByName}` : ''}${o.cancelledAt ? ` em ${formatDateTime(o.cancelledAt)}` : ''}: só para consulta. Motivo: ${o.cancelReason ?? '—'}`
  if (o.status === 'Received' && o.closedBalanceAmount != null) return `Pedido recebido, ${closedLabel(o.closedBalanceAmount)}: só para consulta. Motivo: ${o.closeReason ?? '—'}`
  if (o.status === 'PendingApproval' && !o.canDecide) return o.decideBlocker ?? 'Aguardando aprovação: o pedido não muda até a decisão, salvo se você editar (volta para a aprovação).'
  return null
}

/**
 * Pedido de compra (CMP-03a): cabeçalho com número, chip, fornecedor e destino; fatos (total, previsão de entrega, condição, recebido);
 * abas na URL: Itens, Decisões, Financeiro (com finance.view), Documentos e Histórico de alterações; ações pela situação.
 */
export function PurchaseOrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { criado } = useSearch({ strict: false }) as { criado?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canFinance = useCan('finance.view')
  const query = useGetPurchaseOrder(id)
  const [steps, setSteps] = useState<Step[]>([])
  const [createdShown, setCreatedShown] = useState(!!criado)

  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Pedido não encontrado.' : 'Não foi possível carregar o pedido.'} message={notFound ? 'Ele pode ter sido aberto em outra empresa. Volte à lista.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const o = query.data
  const set = (saved: PurchaseOrderDetail) => client.setQueryData(getGetPurchaseOrderQueryKey(id), saved)
  const refresh = () => client.invalidateQueries({ queryKey: getGetPurchaseOrderQueryKey(id) })
  const go = (to: string, search?: Record<string, unknown>) => () => void navigate({ to, search } as never)
  const shown: Step[] = steps.length ? steps : createdShown && o.status === 'Draft' && o.canSubmit
    ? [{ label: 'Ver solicitações', onSelect: go('/app/compras/solicitacoes') }, { label: 'Novo pedido', onSelect: go('/app/compras/pedidos/novo') }]
    : []
  const band = readOnlyBand(o)
  const late = lateLabel(o.daysLate)

  return (
    <>
      <PageHeading title={`${o.number} — ${o.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={ORDER_STATUS[o.status].tone}>{ORDER_STATUS[o.status].label}</StatusChip>
            {late ? <StatusChip tone="crit">{late}</StatusChip> : null}
            <span>{orderDestination(o.projectNumber, o.projectName)}</span>
            {o.selfApproved ? <Tag>Autoaprovado</Tag> : null}
            <Link to={'/app/compras/pedidos' as never} className="font-semibold text-primary">Voltar aos pedidos</Link>
          </p>
          <OrderActions order={o} set={set} refresh={refresh} onSubmitted={() => { setCreatedShown(false); setSteps([]) }}
            onApproved={(last) => setSteps(last
              ? [
                ...(canFinance ? [{ label: 'Ver previsão no fluxo de caixa', onSelect: go('/app/financeiro/fluxo-de-caixa') }] : []),
                { label: 'Próximo pedido aguardando você', onSelect: go('/app/compras/pedidos', { filtro: 'aguardando-voce' }) },
              ]
              : [{ label: 'Próximo pedido aguardando você', onSelect: go('/app/compras/pedidos', { filtro: 'aguardando-voce' }) }])}
            onClosed={() => setSteps([{ label: 'Ver solicitações', onSelect: go('/app/compras/solicitacoes') }, { label: 'Ver recebimentos', onSelect: go('/app/compras/recebimentos', { aba: 'recebidos' }) }])} />
        </div>
      </PageHeading>
      {shown.length ? <div className="mb-3"><NextSteps steps={shown} onDismiss={() => { setSteps([]); setCreatedShown(false) }} /></div> : null}
      {createdShown && o.status === 'Draft' && o.canSubmit ? <p role="status" className="mb-3 text-sm">Pedido criado em rascunho. Confira e clique em <b>Enviar para aprovação</b>.</p> : null}
      {o.changedAfterSent && o.status !== 'Cancelled' ? <p role="note" className="mb-3 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">Alterado depois do envio: reenvie ao fornecedor.</p> : null}
      {o.status === 'Draft' && o.rejectionComment ? <p role="note" className="mb-3 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">Reprovado: {o.rejectionComment} Ajuste e envie de novo para aprovação.</p> : null}
      {o.status === 'PendingApproval' && o.splitWarning ? <p role="note" className="mb-3 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{o.splitWarning}</p> : null}
      {o.singleQuoteWarning && o.status === 'PendingApproval' ? <p role="note" className="mb-3 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{o.singleQuoteWarning}</p> : null}
      {band ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{band}</p> : null}
      <FactsBar label="Fatos do pedido" facts={[
        { label: 'Total', value: <Money value={o.total} className="font-semibold" /> },
        { label: 'Previsão de entrega', value: <span className="tabular-nums">{formatDate(o.expectedDate)}</span> },
        { label: 'Condição', value: o.paymentTermName ?? '—' },
        { label: 'Recebido', value: (o.amountReceived ?? 0) > 0 ? <Money value={o.amountReceived ?? 0} /> : <span className="tabular-nums">{o.quantityReceived > 0 ? formatQuantity(o.quantityReceived) : 'Nada ainda'}</span> },
      ]} />
      {o.status === 'PendingApproval' ? <div className="mb-3"><OrderLevels levels={o.levels} /></div> : null}
      {o.projectId && o.status !== 'Cancelled' && o.status !== 'Draft' ? (
        <p className="mb-3 text-xs text-muted-foreground">No custo do projeto, este pedido aparece em <b>Comprometido</b>: {ORDER_DICAS.comprometido}</p>
      ) : null}
      {o.notes ? <p className="mb-3 text-sm"><span className="text-muted-foreground">Observação:</span> {o.notes}</p> : null}
      <UrlTabs label="Seções do pedido" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <OrderItems order={o} set={set} refresh={refresh} /> },
        { id: 'decisoes', label: 'Decisões', content: <OrderDecisions orderId={o.id} /> },
        { id: 'recebimentos', label: 'Recebimentos', content: <OrderReceipts order={o} /> },
        { id: 'financeiro', label: 'Financeiro', disabled: !canFinance, content: <OrderPayables orderId={o.id} /> },
        { id: 'documentos', label: 'Documentos', content: <OrderDocuments orderId={o.id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <OrderHistory orderId={o.id} /> },
      ]} />
      <p className="mt-3 text-xs text-muted-foreground">Pedido por {o.requestedByName ?? '—'} em {formatDateTime(o.createdAt)}{o.approvedAt ? ` · aprovado em ${formatDateTime(o.approvedAt)}` : ''}</p>
    </>
  )
}
