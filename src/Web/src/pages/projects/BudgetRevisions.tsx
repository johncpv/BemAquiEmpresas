import { useListProjectBudgetRevisions } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { costLabel } from './margins/margins'

/**
 * Histórico das edições do custo orçado (COM-09 CA 6), da mais recente à mais antiga: quem, quando, o motivo e, em cada natureza
 * alterada, o valor anterior e o novo. Sem edição, diz de onde o orçado veio.
 */
export function BudgetRevisions({ projectId }: { projectId: string }) {
  const query = useListProjectBudgetRevisions(projectId)
  const revisions = Array.isArray(query.data) ? query.data : []
  return (
    <section aria-labelledby="budget-revisions-title" className="space-y-2">
      <h3 id="budget-revisions-title" className="text-sm font-semibold">Edições do custo orçado</h3>
      {query.isPending ? <Skeleton className="h-16" /> : null}
      {query.isError ? <ErrorState title="Não foi possível carregar as edições do custo orçado." onRetry={() => query.refetch()} /> : null}
      {query.isSuccess && revisions.length === 0 ? <p className="text-muted-foreground">Nenhuma edição: o custo orçado é o do orçamento aprovado e dos aditivos.</p> : null}
      {revisions.length > 0 ? (
        <ol className="space-y-2" aria-label="Edições do custo orçado">
          {revisions.map((r) => (
            <li key={`${r.changedAt}-${r.changedBy ?? ''}`} className="rounded-md border border-border bg-card p-3">
              <p className="flex flex-wrap justify-between gap-2">
                <b className="font-semibold">{r.changedByName ?? 'Usuário não identificado'}</b>
                <time className="tabular-nums text-muted-foreground" dateTime={r.changedAt}>{formatDateTime(r.changedAt)}</time>
              </p>
              <p className="mt-1"><span className="text-muted-foreground">Motivo: </span>{r.reason}</p>
              <ul className="mt-1 text-muted-foreground">
                {r.lines.map((l) => (
                  <li key={l.costType}>{costLabel(l.costType)}: <Money value={l.previousAmount} /> → <Money value={l.newAmount} className="font-semibold text-foreground" /></li>
                ))}
              </ul>
            </li>
          ))}
        </ol>
      ) : null}
    </section>
  )
}
