import { useGetEmployeeTimeline } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'

const money = (v: string | null) => (v === null ? '—' : formatMoney(Number(v)))

/** Linha do tempo (CA 16): trocas de cargo com hr.view; reajustes só com hr.view_sensitive (o servidor filtra). */
export function EmployeeTimeline({ id }: { id: string }) {
  const canSensitive = useCan('hr.view_sensitive')
  const query = useGetEmployeeTimeline(id)
  if (query.isPending) return <Skeleton className="h-48 max-w-3xl" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhuma troca de cargo ou reajuste ainda." description={canSensitive ? 'Use "Trocar cargo" ou "Reajustar salário": cada mudança entra aqui, com a vigência e o motivo.' : 'Use "Trocar cargo": cada mudança entra aqui, com a vigência e o motivo. Os reajustes só aparecem com a permissão Ver dados sensíveis.'} />
  }

  return (
    <ol className="max-w-3xl space-y-3">
      {query.data.map((e) => (
        <li key={e.id} className="rounded-md border border-border bg-card p-3 text-sm">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <b>{e.kind === 'Salary' ? 'Reajuste de salário' : 'Troca de cargo'}</b>
            <span className="tabular-nums text-muted-foreground">Vigência {formatDate(e.effectiveDate)}</span>
          </div>
          <p className="mt-1 tabular-nums">
            {e.kind === 'Salary' ? `${money(e.oldValue)} → ${money(e.newValue)}` : `${e.oldValue ?? '—'} → ${e.newValue}`}
          </p>
          <p className="mt-1 text-muted-foreground">{e.reason} · registrado em {formatDateTime(e.createdAt)}</p>
        </li>
      ))}
    </ol>
  )
}
