import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetGoodsReceiptQueryKey, reverseGoodsReceipt, useGetGoodsReceipt, useGetGoodsReceiptHistory, useListGoodsReceiptPayables } from '@/api/generated/bemaqui'
import type { GoodsReceiptDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { RECEIPT_DICAS, RECEIPT_STATUS } from './receipts'

const PAYABLE_STATUS: Record<string, { label: string; tone: 'ok' | 'warn' | 'neutral' | 'info' }> = {
  Planned: { label: 'Previsto', tone: 'info' }, Open: { label: 'Em aberto', tone: 'warn' }, AwaitingApproval: { label: 'Em OP', tone: 'warn' }, Approved: { label: 'Em OP aprovada', tone: 'warn' },
  PartiallyPaid: { label: 'Pago em parte', tone: 'info' }, Paid: { label: 'Pago', tone: 'ok' }, Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

/** Aba Itens (CA 15, 18): quantidade, lote, local, rateio, custo de entrada e o movimento de estoque (link). Cartões a 400 px. */
function ReceiptItems({ receipt }: { receipt: GoodsReceiptDetail }) {
  return (
    <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm" aria-label="Itens do recebimento">
      {receipt.lines.map((l) => (
        <li key={l.id} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 p-3">
          <span className="min-w-0 flex-1 basis-56"><span className="font-mono">{l.itemCode}</span> {l.itemName}</span>
          <span className="tabular-nums"><b>{formatQuantity(l.quantity)} {l.unitCode}</b>{l.supplierUnit && l.supplierQuantity != null ? <span className="text-muted-foreground"> ({formatQuantity(l.supplierQuantity)} {l.supplierUnit})</span> : null}</span>
          <span className="text-muted-foreground">{l.lotNumber ? `Lote ${l.lotNumber}${l.lotExpiry ? ` · validade ${formatDate(l.lotExpiry)}` : ''}` : 'Sem lote'} · {l.locationName}</span>
          <span className="inline-flex items-center tabular-nums">custo de entrada <Money value={l.unitCost} className="ml-1 font-semibold" /><Dica nome="Custo de entrada" texto={RECEIPT_DICAS.custoEntrada} /></span>
          <span className="basis-full text-xs text-muted-foreground tabular-nums">
            <Money value={l.lineAmount} /> + frete <Money value={l.freightShare} /> + IPI <Money value={l.ipiShare} />
            {l.movementId ? <> · <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: l.movementId } as never} className="font-semibold text-primary">{l.movementNumber ?? 'Movimento'}</Link></> : null}
            {l.reversalMovementId ? <> · <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: l.reversalMovementId } as never} className="font-semibold text-primary">estorno</Link></> : null}
          </span>
        </li>
      ))}
      <li className="grid grid-cols-2 gap-2 p-3 tabular-nums sm:grid-cols-4">
        <span><span className="block text-xs text-muted-foreground">Itens</span><Money value={receipt.subtotal} /></span>
        <span><span className="block text-xs text-muted-foreground">Frete</span><Money value={receipt.freight} /></span>
        <span><span className="block text-xs text-muted-foreground">IPI</span><Money value={receipt.ipiAmount} /></span>
        <span><span className="block text-xs text-muted-foreground">Total</span><Money value={receipt.amount} className="font-semibold" /></span>
      </li>
    </ul>
  )
}

/** Aba Financeiro (com finance.view, CA 8, 11): os títulos em aberto do recebimento por parcela, com o link para a OP. */
function ReceiptPayables({ receiptId }: { receiptId: string }) {
  const query = useListGoodsReceiptPayables(receiptId)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os títulos." onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="Nenhum título." description="Os títulos a pagar nascem com o recebimento, um por parcela da condição do pedido." />
  return (
    <ul className="mt-3 divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Títulos a pagar do recebimento">
      {query.data.map((p) => (
        <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <span className="flex flex-wrap items-center gap-2">
            <Link to={'/app/financeiro/pagar/$id' as never} params={{ id: p.id } as never} className="font-semibold tabular-nums text-primary">{p.number}</Link>
            <span className="text-muted-foreground">parcela {p.installment}</span>
          </span>
          <span className="flex flex-wrap items-center gap-2 tabular-nums">
            {p.dueDate ? formatDate(p.dueDate) : '—'}
            <Money value={p.amount} className="font-semibold" />
            <StatusChip tone={PAYABLE_STATUS[p.status]?.tone ?? 'neutral'}>{PAYABLE_STATUS[p.status]?.label ?? p.status}</StatusChip>
            {p.paymentOrderId ? <Link to={'/app/financeiro/ordens-de-pagamento/$id' as never} params={{ id: p.paymentOrderId } as never} className="text-xs font-semibold text-primary">Ver OP</Link> : null}
          </span>
        </li>
      ))}
    </ul>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Removeu' }
const ENTITY: Record<string, string> = { GoodsReceipt: 'o recebimento', GoodsReceiptLine: 'uma linha' }

/** Aba Histórico de alterações (CA 18): antes e depois do recebimento e das linhas, com usuário e data. */
function ReceiptHistory({ receiptId }: { receiptId: string }) {
  const [page, setPage] = useState(1)
  const query = useGetGoodsReceiptHistory(receiptId, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="O registro e o estorno aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div className="mt-3">
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'Sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center justify-between text-xs text-muted-foreground">
          <span className="tabular-nums">Página {data.page} de {pages}</span>
          <span className="flex gap-1">
            <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Anterior</button>
            <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Próxima</button>
          </span>
        </div>
      ) : null}
    </div>
  )
}

/**
 * Recebimento (CMP-04 CA 5, 7, 11, 12, 15 e 18; `/app/compras/recebimentos/$id`): modo leitura com número, chip, pedido e fornecedor;
 * abas Itens, Financeiro (com finance.view) e Histórico de alterações; "Estornar recebimento" com purchasing.approve. Depois de registrar,
 * os próximos passos: "Conferir título a pagar", "Ver movimentos de estoque" e "Ver OS que receberam material".
 */
export function GoodsReceiptPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { registrado } = useSearch({ strict: false }) as { registrado?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canFinance = useCan('finance.view')
  const query = useGetGoodsReceipt(id)
  const [stepsShown, setStepsShown] = useState(!!registrado)
  const [reason, setReason] = useState('')

  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Recebimento não encontrado.' : 'Não foi possível carregar o recebimento.'} message={notFound ? 'Ele pode ter sido aberto em outra empresa. Volte à lista.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const r = query.data
  const go = (to: string, params?: Record<string, string>, search?: Record<string, unknown>) => () => void navigate({ to, params, search } as never)
  const firstMovement = r.lines.find((l) => l.movementId)?.movementId
  const steps: Step[] = stepsShown && r.status === 'Completed' ? [
    ...(canFinance ? [{ label: 'Conferir título a pagar', onSelect: go('/app/compras/recebimentos/$id', { id: r.id }, { aba: 'financeiro' }) }] : []),
    ...(firstMovement ? [{ label: 'Ver movimentos de estoque', onSelect: go('/app/estoque/movimentos/$id', { id: firstMovement }) }] : []),
    ...(r.servedOrders.length ? [{ label: 'Ver OS que receberam material', onSelect: r.servedOrders.length === 1 ? go('/app/producao/os/$id', { id: r.servedOrders[0].id }) : go('/app/producao/os') }] : []),
  ] : []
  const reverse = (
    <ConfirmDialog destructive size="lg" verb="Estornar recebimento" done="Recebimento estornado" title={`Estornar o ${r.number}?`}
      description="Os títulos a pagar do recebimento são cancelados antes de tudo; depois o material sai do estoque pelo custo da entrada e o saldo volta ao previsto do pedido. Título em ordem de pagamento ou pago impede o estorno."
      impact={<p className="tabular-nums">Cancela <Money value={r.amount} className="font-semibold" /> em títulos a pagar e tira do estoque {r.lines.map((l) => `${formatQuantity(l.quantity)} ${l.unitCode} de ${l.itemName}`).join(', ')}.</p>}
      onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo do estorno escrito.' }]}
      onReload={() => query.refetch()}
      onConfirm={async () => { client.setQueryData(getGetGoodsReceiptQueryKey(id), await reverseGoodsReceipt(r.id, { reason: reason.trim(), version: r.version })) }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Estornar recebimento</button>}>
      <div className="space-y-1">
        <label htmlFor="motivo-estorno" className="text-sm font-medium">Motivo</label>
        <textarea id="motivo-estorno" value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
      </div>
    </ConfirmDialog>
  )

  return (
    <>
      <PageHeading title={`${r.number} — ${r.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={RECEIPT_STATUS[r.status].tone}>{RECEIPT_STATUS[r.status].label}</StatusChip>
            <Link to={'/app/compras/pedidos/$id' as never} params={{ id: r.purchaseOrderId } as never} className="font-semibold text-primary">{r.purchaseOrderNumber}</Link>
            <span>{r.projectNumber ?? 'Estoque geral'}</span>
            {r.soleOwner ? <Tag>Recebido pelo Proprietário único</Tag> : null}
            <Link to={'/app/compras/recebimentos' as never} search={{ aba: 'recebidos' } as never} className="font-semibold text-primary">Voltar aos recebimentos</Link>
          </p>
          {r.canReverse ? reverse : null}
        </div>
      </PageHeading>
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setStepsShown(false)} /></div> : null}
      {r.status === 'Reversed'
        ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">Recebimento estornado{r.reversedByName ? ` por ${r.reversedByName}` : ''}{r.reversedAt ? ` em ${formatDateTime(r.reversedAt)}` : ''}: só para consulta. Motivo: {r.reversalReason ?? '—'}</p>
        : <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">Recebimento registrado: só para consulta. Para corrigir, estorne e registre de novo.</p>}
      <FactsBar label="Fatos do recebimento" facts={[
        { label: 'Total', value: <Money value={r.amount} className="font-semibold" /> },
        { label: 'Recebido em', value: <span className="tabular-nums">{formatDate(r.receivedOn)}</span> },
        { label: 'Itens', value: <span className="tabular-nums">{r.lines.length}</span> },
        { label: 'Quem recebeu', value: r.receivedByName ?? '—' },
      ]} />
      {r.notes ? <p className="mb-3 text-sm"><span className="text-muted-foreground">Observação:</span> {r.notes}</p> : null}
      <UrlTabs label="Seções do recebimento" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <ReceiptItems receipt={r} /> },
        { id: 'financeiro', label: 'Financeiro', disabled: !canFinance, disabledReason: 'Os títulos ficam no Financeiro.', content: <ReceiptPayables receiptId={r.id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <ReceiptHistory receiptId={r.id} /> },
      ]} />
      <p className="mt-3 text-xs text-muted-foreground">Registrado em {formatDateTime(r.createdAt)}</p>
    </>
  )
}
