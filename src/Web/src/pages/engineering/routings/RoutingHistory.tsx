import { useState } from 'react'
import { useGetRoutingTemplateHistory } from '@/api/generated/bemaqui'
import { describeChanges } from '@/components/history/describe-changes'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { HISTORY_ENTITY } from './routings'

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }

/** Histórico do roteiro com as etapas e os vínculos de produto (CA 18): antes e depois, usuário e data. */
export function RoutingHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetRoutingTemplateHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças no roteiro, nas etapas e nos produtos aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{HISTORY_ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
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