import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteQuote, getGetQuoteCostsQueryKey, getGetQuoteQueryKey, markQuoteSent, useGetQuote, useGetQuoteCosts } from '@/api/generated/bemaqui'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ItemsTab } from './ItemsTab'
import { QuoteHeaderTab } from './QuoteHeaderTab'
import { QUOTE_STATUS } from './quotes'
import { QuoteTotals } from './QuoteTotals'
import { RefreshCostsDialog } from './RefreshCostsDialog'

/** Orçamento (COM-01a): abas Dados, Itens e Histórico; rodapé fixo de totais; custo só com quotes.view_cost. */

export function QuotePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { novo } = useSearch({ strict: false }) as { novo?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canCost = useCan('quotes.view_cost')
  const canEdit = useCan('quotes.edit')
  const canDelete = useCan('quotes.delete')
  const query = useGetQuote(id)
  const costs = useGetQuoteCosts(id, { query: { enabled: canCost } })
  const [steps, setSteps] = useState(novo === '1')
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
      {quote.status === 'Sent' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Orçamento enviado: só para consulta. Para mudar, crie uma nova revisão.</p> : null}
      <UrlTabs label="Seções do orçamento" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', content: <QuoteHeaderTab key={quote.version} quote={quote} editable={editable} refresh={refresh} /> },
        { id: 'itens', label: 'Itens', content: <ItemsTab quote={quote} costs={costs.data} editable={editable} refresh={refresh} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Quote" id={id} /> },
      ]} />
      <QuoteTotals quote={quote} costs={canCost ? costs.data : undefined} />
    </>
  )
}
