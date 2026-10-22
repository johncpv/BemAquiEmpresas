import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useGetQualityQueue } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { QualityInspection } from './QualityInspection'
import { QUALITY_DICAS, refreshQuality } from './quality'

const action = 'inline-flex min-h-9 items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'
const pill = 'rounded-full border px-3 py-1 text-sm font-semibold aria-pressed:border-primary aria-pressed:bg-primary-soft'

/**
 * Fábrica → Produção → Qualidade (/app/producao/qualidade, CA 2): as OS aguardando qualidade, no filtro "Aguardando você" (as que
 * eu posso decidir, sem a segregação do CA 5) ou "Todas" (?filtro=todas). Cada linha leva à inspeção. Só com production.approve_quality.
 */
export function QualityQueuePage() {
  const navigate = useNavigate()
  const { filtro } = useSearch({ strict: false }) as { filtro?: string }
  const all = filtro === 'todas'
  const query = useGetQualityQueue({ filter: all ? 'todas' : undefined }, { query: { retry: false } })
  useProductionLive(refreshQuality)
  const setFilter = (todas: boolean) => void navigate({ to: '/app/producao/qualidade', search: todas ? { filtro: 'todas' } : {} } as never)
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) {
    body = query.error instanceof ApiError && query.error.status === 403
      ? <EmptyState title="Você não decide a qualidade." description="A fila de qualidade é de quem tem a ação Aprovar ou reprovar a qualidade. Peça a quem administra os acessos." />
      : <ErrorState title="Não foi possível carregar a fila de qualidade." onRetry={() => query.refetch()} />
  } else if (query.data.items.length === 0) {
    body = (
      <EmptyState title="Nenhuma OS aguardando qualidade." description={!all && query.data.totalCount > 0 ? 'As OS na fila tiveram uma etapa feita por você: outra pessoa decide.' : 'Quando a etapa anterior à de qualidade terminar, a OS aparece aqui.'}
        action={!all && query.data.totalCount > 0 ? <button type="button" className={action} onClick={() => setFilter(true)}>Ver todas</button> : <Link to="/app/producao/kanban" className={action}>Ver o Kanban</Link>} />
    )
  } else {
    body = (
      <div className="overflow-x-auto rounded-md border border-border bg-card">
        <table className="w-full text-sm">
          <caption className="sr-only">OS aguardando qualidade</caption>
          <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
            <tr><th className="p-2">OS</th><th className="p-2">Etapa</th><th className="p-2 max-sm:hidden">Projeto</th><th className="p-2 max-sm:hidden">Prometido</th><th className="p-2 max-sm:hidden">Aguardando desde</th><th className="p-2"><span className="sr-only">Ações</span></th></tr>
          </thead>
          <tbody className="divide-y divide-border">
            {query.data.items.map((i) => (
              <tr key={i.stepId} className="align-top">
                <td className="p-2">
                  <Link to="/app/producao/os/$id" params={{ id: i.orderId }} search={{ aba: 'qualidade' } as never} className="font-semibold text-primary tabular-nums hover:underline">{i.orderNumber}</Link>
                  <span className="block text-xs text-muted-foreground">{i.productName}</span>
                </td>
                <td className="p-2">
                  {i.stepCode} · {i.stepName}
                  <span className="mt-1 flex flex-wrap gap-1">
                    <span className="text-xs tabular-nums text-muted-foreground">Tentativa {i.attempt}</span>
                    {i.openNonConformities > 0 ? <StatusChip tone="crit">NC aberta</StatusChip> : null}
                    {i.selfInspection ? <span className="inline-flex items-center"><StatusChip tone="warn">Autoinspeção</StatusChip><Dica nome="Autoinspeção" texto={QUALITY_DICAS.autoinspecao} /></span> : null}
                    {!i.canDecide ? <StatusChip tone="neutral">Você fez parte do trabalho</StatusChip> : null}
                  </span>
                </td>
                <td className="p-2 max-sm:hidden">{i.projectNumber} · {i.projectName}<span className="block text-xs text-muted-foreground">{i.partyName}</span></td>
                <td className="p-2 tabular-nums max-sm:hidden">{i.promisedDate ? formatDate(i.promisedDate) : '—'}</td>
                <td className="p-2 tabular-nums max-sm:hidden">{i.waitingSince ? formatDateTime(i.waitingSince) : '—'}</td>
                <td className="p-2"><Link to="/app/producao/qualidade/$stepId" params={{ stepId: i.stepId }} className={action}>{i.canDecide ? 'Inspecionar' : 'Ver'}</Link></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }
  return (
    <>
      <PageHeading title="Produção: Qualidade" />
      <ProductionTabs />
      <div role="group" aria-label="Filtro da fila" className="mb-3 flex flex-wrap gap-2">
        <button type="button" className={pill} aria-pressed={!all} onClick={() => setFilter(false)}>Aguardando você{query.data ? ` (${query.data.mineCount})` : ''}</button>
        <button type="button" className={pill} aria-pressed={all} onClick={() => setFilter(true)}>Todas{query.data ? ` (${query.data.totalCount})` : ''}</button>
      </div>
      {body}
    </>
  )
}

/** Inspeção pelo backoffice (/app/producao/qualidade/$stepId): a mesma decisão do tablet. */
export function QualityStepPage() {
  const { stepId } = useParams({ strict: false }) as { stepId: string }
  return (
    <>
      <PageHeading title="Produção: Inspeção de qualidade" />
      <ProductionTabs />
      <div className="max-w-3xl"><QualityInspection stepId={stepId} queue="/app/producao/qualidade" /></div>
    </>
  )
}