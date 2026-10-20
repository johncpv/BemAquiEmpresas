import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteQuote, getGetQuoteCostsQueryKey, getGetQuoteQueryKey, markQuoteSent, useGetQuote, useGetQuoteContract, useGetQuoteCosts } from '@/api/generated/bemaqui'
import type { ApprovalResult } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ApproveDialog } from './ApproveDialog'
import { ConditionsTab } from './ConditionsTab'
import { CopyQuoteDialog } from './CopyQuoteDialog'
import { ItemsTab } from './ItemsTab'
import { LoseDialog } from './LoseDialog'
import { QuoteHeaderTab } from './QuoteHeaderTab'
import { QUOTE_STATUS } from './quotes'
import { QuoteTotals } from './QuoteTotals'
import { RefreshCostsDialog } from './RefreshCostsDialog'
import { RevisionActions } from './RevisionActions'
import { RevisionsTab } from './RevisionsTab'

/** Orçamento (COM-01a): abas Dados, Itens e Histórico; rodapé fixo de totais; custo só com quotes.view_cost. */

export function QuotePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { novo, aprovar } = useSearch({ strict: false }) as { novo?: string; aprovar?: string | number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canCost = useCan('quotes.view_cost')
  const canEdit = useCan('quotes.edit')
  const canDelete = useCan('quotes.delete')
  const canApprove = useCan('quotes.approve')
  const query = useGetQuote(id)
  const costs = useGetQuoteCosts(id, { query: { enabled: canCost } })
  const [steps, setSteps] = useState(novo === '1')
  const [approved, setApproved] = useState<ApprovalResult | null>(null)
  const contract = useGetQuoteContract(id, { query: { enabled: query.data?.status === 'Approved' } })
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o orçamento." onRetry={() => query.refetch()} />
  const quote = query.data
  const editable = canEdit && quote.status === 'Draft'
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetQuoteQueryKey(id) })
    if (canCost) await client.invalidateQueries({ queryKey: getGetQuoteCostsQueryKey(id) })
  }

  return (
    <>
      <PageHeading title={`${quote.number}${quote.revision > 0 ? ` rev. ${quote.revision}` : ''} — ${quote.partyName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <StatusChip tone={QUOTE_STATUS[quote.status]?.tone ?? 'neutral'}>{QUOTE_STATUS[quote.status]?.label ?? quote.status}</StatusChip>
          <span className="flex flex-wrap gap-2">
            {canEdit && quote.isCurrent && (quote.status === 'Sent' || quote.status === 'Expired') ? <RevisionActions quote={quote} refresh={refresh} /> : null}
            {quote.status === 'Sent' && canApprove && quote.isCurrent
              ? <ApproveDialog quote={quote} {...(String(aprovar) === '1' ? { open: true, onOpenChange: (v: boolean) => { if (!v) void navigate({ to: '/app/orcamentos/$id', params: { id }, search: {} } as never) } } : {})} onApproved={async (r) => { setApproved(r); await navigate({ to: '/app/orcamentos/$id', params: { id }, search: {} } as never); await refresh() }} />
              : null}
            {quote.status === 'Sent' && canEdit && quote.isCurrent ? <LoseDialog quoteId={id} version={quote.version} onDone={refresh} /> : null}
            {editable ? <CopyQuoteDialog mode="items" quoteId={id} version={quote.version} onDone={refresh} /> : null}
            {editable && canCost ? <RefreshCostsDialog quoteId={id} version={quote.version} onDone={refresh} /> : null}
            {editable ? (
              <ConfirmDialog verb="Marcar como enviado" done="Marcado como enviado" title="Marcar o orçamento como enviado?" description="Depois de enviado, itens, custos e preços ficam só para consulta."
                preconditions={[{ ok: quote.items.some((i) => i.unitPrice > 0), text: 'Ao menos um item com preço.' }]}
                onConfirm={async () => { await markQuoteSent(id, { version: quote.version }); await refresh() }}
                trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Marcar como enviado</button>} />
            ) : null}
            {canDelete && quote.status === 'Draft' && quote.revision === 0 ? (
              <ConfirmDialog destructive verb="Excluir orçamento" done="Orçamento excluído" title="Excluir este rascunho?" description="O número não volta para a sequência."
                onConfirm={async () => { await deleteQuote(id, { version: quote.version }); await navigate({ to: '/app/orcamentos' } as never) }}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Excluir orçamento</button>} />
            ) : null}
          </span>
        </div>
      </PageHeading>
      {steps ? <div className="mb-4"><NextSteps onDismiss={() => setSteps(false)} steps={[{ label: 'Adicionar item', onSelect: () => { setSteps(false); document.querySelector<HTMLInputElement>('input[placeholder^="Ex.: Elevador"]')?.focus() } }]} /></div> : null}
      {costs.data?.staleLaborCosts && quote.copiedFromNumber ? <p role="note" className="mb-4 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Custos copiados de {quote.copiedFromNumber}. Confira com 'Atualizar custos'.</p> : null}
      {approved ? (
        <div className="mb-4"><NextSteps onDismiss={() => setApproved(null)} steps={[
          { label: 'Ver projeto criado', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: approved.projectId } } as never) },
          { label: 'Ver contrato', onSelect: () => void navigate({ to: '/app/contratos/$id', params: { id: approved.contractId } } as never) },
          { label: 'Anexar contrato assinado', onSelect: () => void navigate({ to: '/app/contratos/$id', params: { id: approved.contractId }, search: { aba: 'documentos' } } as never) },
        ]} /></div>
      ) : null}
      {quote.status === 'Approved' ? (
        <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">
          Orçamento aprovado: só para consulta. Ajustes no contrato são feitos por aditivo.
          {contract.data?.contractId ? <> <button type="button" className="font-semibold text-primary hover:underline" onClick={() => void navigate({ to: '/app/contratos/$id', params: { id: contract.data!.contractId! } } as never)}>Ver contrato</button></> : null}
        </p>
      ) : null}
      {quote.status === 'Rejected' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Orçamento perdido{quote.lostDetail ? `: ${quote.lostDetail}` : ''}. Só para consulta; dá para copiar para um novo.</p> : null}
      {!quote.isCurrent ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Há uma revisão mais recente deste orçamento. Esta fica só para consulta; veja a aba Revisões.</p> : null}
      {quote.status === 'Sent' && quote.isCurrent ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Orçamento enviado: só para consulta. Para mudar, crie uma nova revisão.</p> : null}
      <UrlTabs label="Seções do orçamento" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', content: <QuoteHeaderTab key={quote.version} quote={quote} editable={editable} refresh={refresh} /> },
        { id: 'itens', label: 'Itens', content: <ItemsTab quote={quote} costs={costs.data} editable={editable} refresh={refresh} /> },
        { id: 'condicoes', label: 'Condições', content: <ConditionsTab quote={quote} editable={editable} refresh={refresh} /> },
        { id: 'revisoes', label: 'Revisões', content: <RevisionsTab quoteId={id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Quote" id={id} /> },
      ]} />
      <QuoteTotals quote={quote} costs={canCost ? costs.data : undefined} />
    </>
  )
}
