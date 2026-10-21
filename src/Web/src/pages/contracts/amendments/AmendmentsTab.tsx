import { Link } from '@tanstack/react-router'
import { useListAmendments } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { AMENDMENT_STATUS } from './amendments'

/** Aditivos do contrato (COM-07): lista e "Novo aditivo" com projects.edit no contrato ativo. */
export function AmendmentsTab({ contractId, active }: { contractId: string; active: boolean }) {
  const query = useListAmendments(contractId)
  const canEdit = useCan('projects.edit') && active
  const create = canEdit ? <Link to={'/app/contratos/$id/aditivos/$aditivoId' as never} params={{ id: contractId, aditivoId: 'novo' } as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo aditivo</Link> : null
  if (query.isPending) return <Skeleton className="h-32" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="Nenhum aditivo. Use quando o cliente mudar escopo, valor ou prazo." action={create} />
  return (
    <div className="space-y-3">
      {create}
      <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Aditivos do contrato">
        {query.data.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
            <span><Link to={'/app/contratos/$id/aditivos/$aditivoId' as never} params={{ id: contractId, aditivoId: a.id } as never} className="font-semibold text-primary hover:underline">Aditivo {a.number}</Link> <span className="text-muted-foreground">{a.description}</span></span>
            <span className="flex items-center gap-2">
              {a.daysDelta ? <span className="tabular-nums">{a.daysDelta > 0 ? '+' : ''}{a.daysDelta} dias</span> : null}
              <Money value={a.valueDelta} />
              <span className="tabular-nums text-muted-foreground">{formatDate(a.createdAt.slice(0, 10))}</span>
              <StatusChip tone={AMENDMENT_STATUS[a.status]?.tone ?? 'neutral'}>{AMENDMENT_STATUS[a.status]?.label ?? a.status}</StatusChip>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}
