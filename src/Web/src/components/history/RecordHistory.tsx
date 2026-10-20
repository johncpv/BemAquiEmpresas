import { useState } from 'react'
import { useGetAuditHistory } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { describeChanges } from './describe-changes'

const ACTIONS: Record<string, string> = { Created: 'Criou', SensitiveRead: 'Leitura de dado sensível', Updated: 'Alterou', Deleted: 'Excluiu' }

/**
 * Histórico de um registro auditado (FND-09c CA 13): quem, quando (Brasília) e o que mudou, paginado. Sem usuário, mostra a
 * origem gravada (ex.: "Importação Dolibarr · operador · execução", MIG-01a CA 21).
 */
export function RecordHistory({ entity, id }: { entity: string; id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetAuditHistory(entity, id, { page, pageSize: 20 })

  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (!data || data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças neste registro aparecem aqui, com quem fez e quando." />

  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">
              {describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}
            </ul>
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
