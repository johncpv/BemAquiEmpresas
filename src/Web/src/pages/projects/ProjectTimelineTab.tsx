import { Link } from '@tanstack/react-router'
import { useGetProjectTimeline } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'

/** Linha do tempo (COM-05 CA 12): eventos do projeto em ordem; os financeiros só com finance.view (o servidor filtra). */
export function ProjectTimelineTab({ projectId }: { projectId: string }) {
  const query = useGetProjectTimeline(projectId)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a linha do tempo." onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="Nada aconteceu ainda neste projeto." />
  return (
    <ol className="space-y-2 text-sm" aria-label="Linha do tempo do projeto">
      {query.data.map((e, i) => (
        <li key={`${e.kind}-${i}`} className="rounded-md border border-border bg-card p-3">
          <p className="flex flex-wrap justify-between gap-2">
            {e.link ? <Link to={e.link as never} className="font-semibold text-primary hover:underline">{e.title}</Link> : <b>{e.title}</b>}
            <span className="tabular-nums text-muted-foreground">{formatDateTime(e.at)}</span>
          </p>
          <p className="text-xs text-muted-foreground">{e.who ?? 'Sistema'}</p>
        </li>
      ))}
    </ol>
  )
}
