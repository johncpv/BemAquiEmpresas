import { useState } from 'react'
import { useGetFabricationOrderHistory } from '@/api/generated/bemaqui'
import { describeChanges } from '@/components/history/describe-changes'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }
const ENTITY: Record<string, string> = {
  ProductionOrder: 'OS', ProductionOrderStep: 'Etapa', ProductionOrderChecklistItem: 'Item do checklist', LaborEntry: 'Apontamento', StepPause: 'Pausa',
  QualityDecision: 'Decisão de qualidade', NonConformity: 'Não conformidade',
}

/** Histórico da OS com as etapas e o checklist (CA 25): antes e depois, usuário e data. */
export function FabricationHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetFabricationOrderHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças na OS e nas etapas aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center gap-2 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
          <span className="tabular-nums">{page} de {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
        </div>
      ) : null}
    </div>
  )
}