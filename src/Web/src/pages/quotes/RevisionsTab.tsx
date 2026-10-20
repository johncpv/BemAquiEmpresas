import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useListQuoteCosts, useListQuoteRevisions } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { QUOTE_STATUS } from './quotes'

/** Aba Revisões (COM-02 CA 8): todas as revisões do número; escolha duas para comparar. */
export function RevisionsTab({ quoteId }: { quoteId: string }) {
  const query = useListQuoteRevisions(quoteId)
  const canCost = useCan('quotes.view_cost')
  const ids = (query.data ?? []).map((r) => r.id).join(',')
  const costs = useListQuoteCosts({ ids }, { query: { enabled: canCost && ids.length > 0 } })
  const [picked, setPicked] = useState<string[]>([])
  if (query.isPending) return <Skeleton className="h-32" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length <= 1) return <EmptyState title="Este orçamento ainda não tem outras revisões." />
  const toggle = (id: string) => setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p.slice(-1), id]))
  return (
    <div className="space-y-3">
      <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Revisões do orçamento">
        {query.data.map((r) => (
          <li key={r.id} className={`flex flex-wrap items-center gap-3 p-3 ${r.isCurrent ? 'bg-primary-soft' : ''}`}>
            <input type="checkbox" aria-label={`Comparar a revisão ${r.revision}`} checked={picked.includes(r.id)} onChange={() => toggle(r.id)} className="size-4 accent-primary" />
            <Link to={'/app/orcamentos/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary hover:underline">Rev. {r.revision}</Link>
            <StatusChip tone={QUOTE_STATUS[r.status]?.tone ?? 'neutral'}>{QUOTE_STATUS[r.status]?.label ?? r.status}</StatusChip>
            {r.isCurrent ? <span className="text-xs font-semibold">Atual</span> : null}
            <span className="text-muted-foreground">{formatDateTime(r.createdAt)} · {r.createdByName ?? '—'}</span>
            <span className="ml-auto"><Money value={r.totalPrice} />{canCost && costs.data ? <span className="ml-2 text-xs text-muted-foreground">margem {costs.data.find((c) => c.id === r.id)?.marginPct?.toLocaleString('pt-BR', { maximumFractionDigits: 2 }) ?? '—'}%</span> : null}</span>
          </li>
        ))}
      </ul>
      <Link to={'/app/orcamentos/$id/comparar' as never} params={{ id: picked[0] ?? quoteId } as never} search={{ com: picked[1] } as never} disabled={picked.length !== 2}
        className={`inline-block rounded-md px-3 py-2 text-sm font-semibold ${picked.length === 2 ? 'bg-primary text-primary-foreground' : 'pointer-events-none border border-border opacity-50'}`}>Comparar</Link>
    </div>
  )
}
