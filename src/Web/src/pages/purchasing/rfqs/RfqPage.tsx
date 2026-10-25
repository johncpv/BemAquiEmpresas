import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { cancelRfq, generateRfqPurchaseOrders, getGetRfqQueryKey, getRfq, submitRfqPurchaseOrders, useGetRfq } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqSubmitResult } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatMoney } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { ORDER_STATUS } from '../orders/purchase-orders'
import { RfqComparison } from './RfqComparison'
import { RfqResponses } from './RfqResponses'
import { RfqDocuments, RfqHistory } from './RfqTabs'
import { RFQ_DICAS, RFQ_STATUS } from './rfqs'

const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 text-sm font-semibold'

/** "Cancelar cotação" (CA 12): motivo obrigatório; as solicitações voltam a aberta. */
function CancelRfq({ rfq, set }: { rfq: RfqDetail; set: (r: RfqDetail) => void }) {
  const [reason, setReason] = useState('')
  const linked = rfq.lines.reduce((n, l) => n + l.requests.length, 0)
  return (
    <ConfirmDialog destructive verb="Cancelar cotação" done="Cotação cancelada" title={`Cancelar ${rfq.number}?`} onOpen={() => setReason('')}
      description={linked ? `As ${linked} linhas de solicitação voltam para Aberta, para cotar de novo ou pedir direto.` : 'A cotação fica só para consulta.'}
      onConfirm={async () => {
        if (!reason.trim()) throw new DialogMessage('Escreva o motivo do cancelamento.')
        set(await cancelRfq(rfq.id, { reason: reason.trim(), version: rfq.version }))
      }}
      onReload={async () => set(await getRfq(rfq.id))}
      trigger={<button type="button" className={secondary}>Cancelar cotação</button>}>
      <label className="block text-sm font-medium">Motivo (obrigatório)
        <textarea value={reason} maxLength={500} rows={2} onChange={(e) => setReason(e.target.value)} className={`${inputClass} mt-1`} />
      </label>
    </ConfirmDialog>
  )
}

/** Pedidos da cotação (CA 9, 10): a prévia por vencedor e, gerados, o número, a situação e o resultado do envio. */
function RfqOrders({ rfq, result }: { rfq: RfqDetail; result: RfqSubmitResult | null }) {
  return (
    <section aria-label="Pedidos desta cotação" className="mb-3 space-y-2 rounded-md border border-border bg-card p-3">
      <h2 className="text-sm font-semibold">Pedidos desta cotação</h2>
      <ul className="divide-y divide-border text-sm">
        {rfq.orders.map((o) => {
          const sent = result?.items.find((i) => i.purchaseOrderId === o.order?.id)
          return (
            <li key={o.supplierId} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <span>
                <b>{o.partyName}</b> · {o.lines} {o.lines === 1 ? 'item' : 'itens'} · <Money value={o.order?.total ?? o.total} />
                {sent && !sent.sent ? <span className="block text-xs text-crit-ink">Não enviado: {sent.message}</span> : null}
                {sent?.sent ? <span className="block text-xs text-muted-foreground">Enviado{sent.levels ? ` · ${sent.levels} ${sent.levels === 1 ? 'nível' : 'níveis'}` : ''}{sent.approverName ? ` · ${sent.approverName}` : ''}</span> : null}
              </span>
              {o.order ? (
                <span className="flex flex-wrap items-center gap-2">
                  {sent ? <StatusChip tone={sent.sent ? 'ok' : 'crit'}>{sent.sent ? 'Enviado' : 'Não enviado'}</StatusChip> : <StatusChip tone={ORDER_STATUS[o.order.status].tone}>{ORDER_STATUS[o.order.status].label}</StatusChip>}
                  <Link to={'/app/compras/pedidos/$id' as never} params={{ id: o.order.id } as never} className="font-semibold text-primary hover:underline">Abrir pedido {o.order.number}</Link>
                </span>
              ) : <span className="text-xs text-muted-foreground">Pedido ainda não gerado</span>}
            </li>
          )
        })}
      </ul>
      {result ? <p role="status" className="text-sm font-semibold">{result.message}</p> : null}
    </section>
  )
}

/**
 * Cotação (CMP-02): cabeçalho com número, chip, itens e fornecedores; abas na URL (Respostas, Comparativo, Documentos e Histórico de
 * alterações); concluída, o próximo passo "Gerar pedidos de compra" e depois "Enviar todos para aprovação".
 */
export function RfqPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { criada } = useSearch({ strict: false }) as { criada?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetRfq(id)
  const idempotency = useIdempotencyKey()
  const [generateOpen, setGenerateOpen] = useState(false)
  const [submitOpen, setSubmitOpen] = useState(false)
  const [result, setResult] = useState<RfqSubmitResult | null>(null)
  const [dismissed, setDismissed] = useState(false)

  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Cotação não encontrada.' : 'Não foi possível carregar a cotação.'} message={notFound ? 'Ela pode ter sido aberta em outra empresa. Volte à lista.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const rfq = query.data
  const set = (saved: RfqDetail) => client.setQueryData(getGetRfqQueryKey(id), saved)
  const refresh = async () => set(await getRfq(id))
  const go = (to: string, search?: Record<string, unknown>) => () => void navigate({ to, search } as never)
  const pending = rfq.orders.filter((o) => !o.order)
  const drafts = rfq.orders.filter((o) => o.order?.status === 'Draft')
  const draftsTotal = drafts.reduce((sum, o) => sum + (o.order?.total ?? 0), 0)
  const responded = rfq.suppliers.filter((s) => s.responded).length
  const steps: Step[] = dismissed ? [] : rfq.canGenerateOrders
    ? [{ label: 'Gerar pedidos de compra', onSelect: () => setGenerateOpen(true) }]
    : rfq.canSubmitOrders
      ? [{ label: 'Enviar todos para aprovação', onSelect: () => setSubmitOpen(true) }]
      : rfq.status === 'Open' && criada && responded === 0
        ? [{ label: 'Registrar a primeira resposta', onSelect: go(`/app/compras/cotacoes/${id}`, { aba: 'respostas' }) }]
        : rfq.status === 'Open' && responded > 0
          ? [{ label: 'Comparar e escolher', onSelect: go(`/app/compras/cotacoes/${id}`, { aba: 'comparativo' }) }]
          : result
            ? [{ label: 'Ver pedidos aguardando aprovação', onSelect: go('/app/compras/pedidos', { situacao: 'PendingApproval', filtro: 'todos' }) }, { label: 'Ver solicitações', onSelect: go('/app/compras/solicitacoes') }]
            : []
  const band = rfq.status === 'Cancelled'
    ? `Cotação cancelada${rfq.cancelledByName ? ` por ${rfq.cancelledByName}` : ''}${rfq.cancelledAt ? ` em ${formatDateTime(rfq.cancelledAt)}` : ''}: só para consulta. Motivo: ${rfq.cancelReason ?? '—'}`
    : rfq.status === 'Closed' ? `Cotação concluída${rfq.closedByName ? ` por ${rfq.closedByName}` : ''}${rfq.closedAt ? ` em ${formatDateTime(rfq.closedAt)}` : ''}: os preços e os vencedores não mudam mais. Para cotar de novo, crie outra cotação.` : null

  return (
    <>
      <PageHeadingBlock rfq={rfq} set={set} />
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setDismissed(true)} /></div> : null}
      {rfq.singleQuoteWarning ? (
        <p role="note" className="mb-3 inline-flex items-center rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          1 cotação<Dica nome="1 cotação" texto={RFQ_DICAS.umaCotacao} />: o pedido gerado leva o aviso na aprovação, sem bloquear.
        </p>
      ) : null}
      {band ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{band}</p> : null}
      <FactsBar label="Fatos da cotação" facts={[
        { label: 'Itens', value: <span className="tabular-nums">{rfq.lines.length}</span> },
        { label: 'Fornecedores', value: <span className="tabular-nums">{rfq.suppliers.length}</span> },
        { label: 'Respostas', value: <span className="tabular-nums">{responded} de {rfq.suppliers.length}</span> },
        { label: 'Anexo obrigatório acima de', value: <Money value={rfq.attachmentLimit} /> },
      ]} />
      {rfq.status === 'Closed' && rfq.orders.length ? <RfqOrders rfq={rfq} result={result} /> : null}
      {rfq.notes ? <p className="mb-3 text-sm"><span className="text-muted-foreground">Observação:</span> {rfq.notes}</p> : null}
      <UrlTabs label="Seções da cotação" fallback="respostas" tabs={[
        { id: 'respostas', label: 'Respostas', content: <RfqResponses rfq={rfq} set={set} /> },
        { id: 'comparativo', label: 'Comparativo', content: <RfqComparison key={`${rfq.version}`} rfq={rfq} set={set} /> },
        { id: 'documentos', label: 'Documentos', content: <RfqDocuments rfq={rfq} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RfqHistory rfqId={rfq.id} /> },
      ]} />
      <p className="mt-3 text-xs text-muted-foreground">Cotação por {rfq.createdByName ?? '—'} em {formatDateTime(rfq.createdAt)}</p>

      <ConfirmDialog open={generateOpen} onOpenChange={setGenerateOpen} size="lg" verb="Gerar pedidos de compra" done="Pedidos gerados"
        title={`Gerar ${pending.length} ${pending.length === 1 ? 'pedido' : 'pedidos'} de compra?`}
        description="Um pedido em rascunho por fornecedor vencedor, com as linhas vencedoras, o frete, o IPI, a condição e a previsão pelo prazo da resposta. As solicitações passam a Pedida."
        impact={<ul className="space-y-1 tabular-nums">{pending.map((o) => <li key={o.supplierId}>{o.partyName}: {o.lines} {o.lines === 1 ? 'item' : 'itens'}, <Money value={o.total} className="font-semibold" /></li>)}</ul>}
        onConfirm={async () => {
          await generateRfqPurchaseOrders(id, { version: rfq.version }, idempotency.options)
          idempotency.renew()
          setDismissed(false)
          await refresh()
        }}
        onReload={refresh} />
      <ConfirmDialog open={submitOpen} onOpenChange={setSubmitOpen} size="lg" verb="Enviar todos para aprovação" done="Pedidos enviados para aprovação"
        title={`Enviar ${drafts.length} ${drafts.length === 1 ? 'pedido' : 'pedidos'}, total ${formatMoney(draftsTotal)}, para aprovação?`}
        description="Cada pedido passa pela própria alçada, de forma independente. O pedido sem aprovador possível continua em rascunho, com o motivo."
        impact={<ul className="space-y-1 tabular-nums">{drafts.map((o) => <li key={o.supplierId}>{o.order?.number} · {o.partyName}: <Money value={o.order?.total ?? 0} className="font-semibold" /></li>)}</ul>}
        onConfirm={async () => {
          setResult(await submitRfqPurchaseOrders(id))
          setDismissed(false)
          await refresh()
        }} />
    </>
  )
}

function PageHeadingBlock({ rfq, set }: { rfq: RfqDetail; set: (r: RfqDetail) => void }) {
  const items = rfq.lines.length ? `${rfq.lines[0].itemCode} ${rfq.lines[0].itemName}${rfq.lines.length > 1 ? ` e mais ${rfq.lines.length - 1}` : ''}` : '—'
  return (
    <PageHeading title={`${rfq.number} — ${items}`}>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
        <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusChip tone={RFQ_STATUS[rfq.status].tone}>{RFQ_STATUS[rfq.status].label}</StatusChip>
          <span>{rfq.suppliers.map((s) => s.partyName).join(', ')}</span>
          <Link to={'/app/compras/cotacoes' as never} className="font-semibold text-primary">Voltar às cotações</Link>
        </p>
        {rfq.canCancel ? <CancelRfq rfq={rfq} set={set} /> : null}
      </div>
    </PageHeading>
  )
}
