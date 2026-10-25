import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import {
  correctPurchaseOrderPrice, getDownloadAttachmentUrl, getListAttachmentsQueryKey, uploadAttachment, useGetPurchaseOrderDecisions, useGetPurchaseOrderHistory, useListAttachments,
  useListPurchaseOrderPayables, useListPurchaseOrderReceipts,
} from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail, PurchaseOrderLevelView, PurchaseOrderLineView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { describeChanges } from '@/components/history/describe-changes'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'
import { RECEIPT_STATUS } from '../receipts/receipts'
import { DECISION, LEVEL_STATUS, ORDER_DICAS } from './purchase-orders'

const OWNER = 'PurchaseOrder'

/**
 * "Corrigir preço" da linha (CMP-04 CA 14, purchasing.approve, motivo obrigatório): vale para o saldo a receber; acima do aprovado mais a
 * tolerância, o pedido volta para a aprovação.
 */
function CorrectPrice({ order, line, set, refresh }: { order: PurchaseOrderDetail; line: PurchaseOrderLineView; set: (o: PurchaseOrderDetail) => void; refresh: () => Promise<unknown> }) {
  const [price, setPrice] = useState('')
  const [reason, setReason] = useState('')
  const value = Number(price.trim().replace(',', '.'))
  const valid = price.trim() !== '' && !Number.isNaN(value) && value > 0 && value !== line.unitPrice
  return (
    <ConfirmDialog verb="Corrigir preço" done="Preço corrigido" title={`Corrigir o preço de ${line.itemCode}?`}
      description={<span className="inline-flex items-center">O preço novo vale para o saldo de {formatQuantity(line.quantityOpen ?? 0)} {line.unitCode}; o que já entrou fica com o preço de antes.<Dica nome="Corrigir preço" texto={ORDER_DICAS.corrigirPreco} /></span>}
      impact={<p className="tabular-nums">Preço atual <Money value={line.unitPrice} />{valid ? <> → <Money value={value} className="font-semibold" /></> : null} · aprovado <Money value={order.approvedTotal ?? order.total} /></p>}
      onOpen={() => { setPrice(''); setReason('') }} preconditions={[{ ok: valid, text: 'Preço novo, maior que zero e diferente do atual.' }, { ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
      onConfirm={async () => { set(await correctPurchaseOrderPrice(order.id, line.id, { unitPrice: value, reason: reason.trim(), version: order.version })) }} onReload={refresh}
      trigger={<button type="button" className="text-xs font-semibold text-primary">Corrigir preço</button>}>
      <div className="space-y-1">
        <label htmlFor={`preco-${line.id}`} className="text-sm font-medium">Preço novo</label>
        <input id={`preco-${line.id}`} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value.replace(/[^0-9,.]/g, ''))} className={inputClass + ' tabular-nums'} />
      </div>
      <div className="space-y-1">
        <label htmlFor={`motivo-preco-${line.id}`} className="text-sm font-medium">Motivo</label>
        <textarea id={`motivo-preco-${line.id}`} value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
      </div>
    </ConfirmDialog>
  )
}

/** Aba Itens (CA 3): cada linha em cartão a 400 px, com as SC atendidas, o recebido e "Corrigir preço" (CMP-04). */
export function OrderItems({ order, set, refresh }: { order: PurchaseOrderDetail; set?: (o: PurchaseOrderDetail) => void; refresh?: () => Promise<unknown> }) {
  return (
    <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm" aria-label="Itens do pedido">
      {order.lines.map((l) => (
        <li key={l.id} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 p-3">
          <span className="min-w-0 flex-1 basis-56">
            <span className="font-mono">{l.itemCode}</span> {l.itemName}
            {l.requests.length ? (
              <span className="mt-1 flex flex-wrap gap-2 text-xs">
                {l.requests.map((r) => <Link key={r.lineId} to={'/app/compras/solicitacoes/$id' as never} params={{ id: r.requestId } as never} className="font-semibold text-primary">{r.number}</Link>)}
              </span>
            ) : null}
          </span>
          <span className="tabular-nums"><b>{formatQuantity(l.quantity)} {l.unitCode}</b>{l.enteredUnitCode && l.enteredQuantity !== null ? <span className="text-muted-foreground"> ({formatQuantity(l.enteredQuantity)} {l.enteredUnitCode})</span> : null}</span>
          <span className="tabular-nums text-muted-foreground">× <Money value={l.unitPrice} /></span>
          <Money value={l.lineTotal} className="font-semibold" />
          {l.quantityReceived > 0 ? <span className="basis-full text-xs text-muted-foreground">Recebido {formatQuantity(l.quantityReceived + (l.quantityOverReceived ?? 0))} {l.unitCode} · saldo {formatQuantity(l.quantityOpen ?? 0)} {l.unitCode}</span> : null}
          {order.canCorrectPrice && set && refresh && (l.quantityOpen ?? 0) > 0 ? <span className="basis-full"><CorrectPrice order={order} line={l} set={set} refresh={refresh} /></span> : null}
        </li>
      ))}
      <li className="grid grid-cols-2 gap-2 p-3 tabular-nums sm:grid-cols-4">
        <span><span className="block text-xs text-muted-foreground">Subtotal</span><Money value={order.subtotal} /></span>
        <span><span className="block text-xs text-muted-foreground">Frete</span><Money value={order.freight} /></span>
        <span><span className="block text-xs text-muted-foreground">IPI ({order.ipiPct.toLocaleString('pt-BR')}%)</span><Money value={order.ipiAmount} /></span>
        <span><span className="block text-xs text-muted-foreground">Total</span><Money value={order.total} className="font-semibold" /></span>
      </li>
    </ul>
  )
}

/** Níveis da tentativa: regra, aprovador, valor de referência e aviso de fracionamento (CA 6, 9). */
export function OrderLevels({ levels }: { levels: PurchaseOrderLevelView[] }) {
  if (levels.length === 0) return null
  return (
    <ol className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Níveis de aprovação">
      {levels.map((l) => (
        <li key={`${l.attempt}-${l.level}`} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <span><b>Nível {l.level}</b> · {l.approverName}</span>
          <span className="flex items-center gap-2">
            <span className="inline-flex items-center text-xs text-muted-foreground">referência&nbsp;<Money value={l.referenceAmount} /><Dica nome="Valor de referência" texto={ORDER_DICAS.referencia} /></span>
            <StatusChip tone={LEVEL_STATUS[l.status].tone}>{LEVEL_STATUS[l.status].label}</StatusChip>
          </span>
        </li>
      ))}
    </ol>
  )
}

/** Aba Decisões (CA 23): níveis e linha do tempo por tentativa, com a regra, o valor de referência, o fracionamento e "Autoaprovado". */
export function OrderDecisions({ orderId }: { orderId: string }) {
  const query = useGetPurchaseOrderDecisions(orderId)
  if (query.isPending) return <Skeleton className="h-32" />
  if (query.isError) return <ErrorState title="Não foi possível carregar as decisões." onRetry={() => query.refetch()} />
  const { levels, events } = query.data
  if (events.length === 0) return <EmptyState title="Nenhuma decisão ainda." description="Envie o pedido para aprovação: o envio, as aprovações e as reprovações aparecem aqui." />
  const attempts = [...new Set(events.map((e) => e.attempt))].sort((a, b) => b - a)
  return (
    <div className="mt-3 space-y-4">
      {attempts.map((attempt) => {
        const mine = levels.filter((l) => l.attempt === attempt)
        const warning = mine.find((l) => l.splitWarning)?.splitWarning
        return (
          <section key={attempt} aria-label={attempt === 0 ? 'Antes do envio' : `Tentativa ${attempt}`} className="space-y-2">
            <h3 className="text-sm font-semibold">{attempt === 0 ? 'Antes do envio' : `Tentativa ${attempt}`}</h3>
            {warning ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{warning}</p> : null}
            <OrderLevels levels={mine} />
            <ol className="space-y-2 text-sm">
              {events.filter((e) => e.attempt === attempt).map((e) => (
                <li key={`${e.decidedAt}-${e.decision}-${e.level}`} className="rounded-md border border-border bg-card p-3">
                  <b>{DECISION[e.decision]}</b>{e.level > 0 ? ` no nível ${e.level}` : ''}{e.approverName ? ` (${e.approverName})` : ''} por {e.decidedByName ?? '—'} em {formatDateTime(e.decidedAt)}
                  {e.selfApproved ? <Tag className="ml-2">Autoaprovado</Tag> : null}
                  {e.referenceAmount != null ? <span className="ml-2 text-xs text-muted-foreground">referência <Money value={e.referenceAmount} /></span> : null}
                  {e.comment ? <p className="mt-1 text-muted-foreground">{e.comment}</p> : null}
                </li>
              ))}
            </ol>
          </section>
        )
      })}
    </div>
  )
}

const PAYABLE_STATUS: Record<string, { label: string; tone: 'ok' | 'warn' | 'neutral' | 'info' }> = {
  Planned: { label: 'Previsto', tone: 'info' }, Open: { label: 'Em aberto', tone: 'warn' }, AwaitingApproval: { label: 'Em OP', tone: 'warn' }, Approved: { label: 'Em OP aprovada', tone: 'warn' },
  PartiallyPaid: { label: 'Pago em parte', tone: 'info' }, Paid: { label: 'Pago', tone: 'ok' }, Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

/** Aba Financeiro (com finance.view): os títulos previstos por parcela, com a marca "Estimado" e o link para a OP. */
export function OrderPayables({ orderId }: { orderId: string }) {
  const query = useListPurchaseOrderPayables(orderId)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os títulos." onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="Nenhum título previsto ainda." description="Os títulos previstos entram no fluxo de caixa quando o pedido é aprovado, um por parcela da condição." />
  return (
    <ul className="mt-3 divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Títulos previstos do pedido">
      {query.data.map((p) => (
        <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <span className="flex flex-wrap items-center gap-2">
            <Link to={'/app/financeiro/pagar/$id' as never} params={{ id: p.id } as never} className="font-semibold tabular-nums text-primary">{p.number}</Link>
            <span className="text-muted-foreground">parcela {p.installment}</span>
            {p.isEstimate && p.status !== 'Cancelled' ? <span className="inline-flex items-center"><Tag>Estimado</Tag><Dica nome="Estimado" texto={ORDER_DICAS.estimado} /></span> : null}
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

/** Aba Recebimentos do pedido (CMP-04 CA 15): os recebimentos registrados e estornados, com o link para cada um. */
export function OrderReceipts({ order }: { order: PurchaseOrderDetail }) {
  const query = useListPurchaseOrderReceipts(order.id)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os recebimentos." onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhum recebimento." description="Quando o material chegar, quem tem a permissão Registrar recebimento dá a entrada pelo botão Registrar recebimento." />
  }

  return (
    <ul className="mt-3 divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Recebimentos do pedido">
      {query.data.map((r) => (
        <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <span className="flex flex-wrap items-center gap-2">
            <Link to={'/app/compras/recebimentos/$id' as never} params={{ id: r.id } as never} className="font-semibold tabular-nums text-primary">{r.number}</Link>
            <span className="tabular-nums text-muted-foreground">{formatDate(r.receivedOn)}</span>
            <span className="text-muted-foreground">{r.itemSummary}</span>
          </span>
          <span className="flex items-center gap-2"><Money value={r.amount} className="font-semibold" /><StatusChip tone={RECEIPT_STATUS[r.status].tone}>{RECEIPT_STATUS[r.status].label}</StatusChip></span>
        </li>
      ))}
    </ul>
  )
}
/** Aba Documentos (CA 21): anexos do dono PurchaseOrder; ler com purchasing.view, anexar com purchasing.edit (pdf, png, jpg até 10 MB). */
export function OrderDocuments({ orderId }: { orderId: string }) {
  const list = useListAttachments(OWNER, orderId)
  const client = useQueryClient()
  const canEdit = useCan('purchasing.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, orderId, { file })
      notifySuccess('Documento anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, orderId) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o documento.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const action = canEdit ? (
    <>
      <input ref={input} id="documento-pedido" type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="documento-pedido" className="inline-block cursor-pointer rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{sending ? 'Enviando…' : 'Anexar documento'}</label>
    </>
  ) : null

  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <EmptyState title="Nenhum documento." description="Anexe a proposta do fornecedor ou outro documento do pedido (PDF, PNG ou JPG até 10 MB)." action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {list.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">{a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}</span>
            </li>
          ))}
        </ul>
        <div className="mt-3">{action}</div>
      </>
    )
  }

  return <div className="mt-3 space-y-2"><FormError message={error} />{body}</div>
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Removeu' }
const ENTITY: Record<string, string> = { PurchaseOrder: 'o pedido', PurchaseOrderLine: 'uma linha', PurchaseOrderPriceCorrection: 'o preço (correção)' }

/** Aba Histórico de alterações (CA 23): antes e depois do pedido e das linhas, com usuário e data. */
export function OrderHistory({ orderId }: { orderId: string }) {
  const [page, setPage] = useState(1)
  const query = useGetPurchaseOrderHistory(orderId, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="Cada mudança no pedido e nas linhas aparece aqui, com quem fez e quando." />
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
