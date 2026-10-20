import { useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { getGetContractQueryKey, getGetProjectQueryKey, useGetProject, useGetProjectBudget } from '@/api/generated/bemaqui'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStage } from '../contracts/contracts'
import { COST_SECTIONS } from '../quotes/quotes'
import { ProjectEventDialog } from './ProjectEventDialog'

const qty = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** Projeto, primeira versão (COM-04a CA 6, 7, 17): cabeçalho com a etapa e os itens; orçado só com projects.view_cost. */
export function ProjectPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const canCost = useCan('projects.view_cost')
  const canEdit = useCan('projects.edit')
  const canApprove = useCan('projects.approve')
  const client = useQueryClient()
  const query = useGetProject(id)
  const budget = useGetProjectBudget(id, { query: { enabled: canCost } })
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o projeto." onRetry={() => query.refetch()} />
  const p = query.data
  const stage = projectStage(p.status ?? '')
  const active = p.contractStatus === 'Active'
  const inProduction = p.status === 'Planned' || p.status === 'InProgress'
  const canFabricate = active && canEdit && inProduction
  const canDeliver = active && canApprove && (p.status === 'Fabricated' || (inProduction && !p.hasFabricationMilestone))
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetProjectQueryKey(id) })
    await client.invalidateQueries({ queryKey: getGetContractQueryKey(p.contractId) })
  }
  return (
    <>
      <PageHeading title={`${p.number} — ${p.name}`}>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          {p.isOnboarding ? <Tag>Implantação</Tag> : null}
          {stage < 0 ? <StatusChip tone="neutral">Cancelado</StatusChip> : <StageIndicator stages={PROJECT_STAGES} letters={PROJECT_LETTERS} current={stage} showLabel />}
          <Link to={'/app/contratos/$id' as never} params={{ id: p.contractId } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver contrato {p.contractNumber}</Link>
          {canFabricate ? <ProjectEventDialog project={p} kind="fabrication" onDone={refresh} /> : null}
          {canDeliver ? <ProjectEventDialog project={p} kind="delivery" onDone={refresh} /> : null}
        </div>
      </PageHeading>
      <dl className="mb-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Cliente</dt><dd>{p.partyName}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Data prometida</dt><dd className="tabular-nums">{p.promisedDate ? formatDate(p.promisedDate) : '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Responsável</dt><dd>{p.managerName ?? '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Obra</dt><dd>{p.siteAddress ?? '—'}</dd></div>
        {p.fabricatedOn ? <div><dt className="text-xs text-muted-foreground">Fabricação concluída</dt><dd className="tabular-nums">{formatDate(p.fabricatedOn)}</dd></div> : null}
        {p.deliveredOn ? <div><dt className="text-xs text-muted-foreground">Entrega</dt><dd className="tabular-nums">{formatDate(p.deliveredOn)}{p.deliveredManually ? ' (registrada à mão)' : ''}</dd></div> : null}
      </dl>
      <UrlTabs label="Seções do projeto" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: p.items.length === 0 ? <EmptyState title="Projeto sem itens." /> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Itens do projeto">
            {p.items.map((i) => <li key={i.id} className="flex flex-wrap justify-between gap-2 p-3"><span><b>{i.sequence}.</b> {i.description}</span><span className="tabular-nums">{qty(i.quantity)}</span></li>)}
          </ul>
        ) },
        ...(canCost ? [{ id: 'orcado', label: 'Orçado', content: budget.isPending ? <Skeleton className="h-24" /> : budget.isError ? <ErrorState onRetry={() => budget.refetch()} /> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Orçado por natureza">
            {budget.data.map((b) => <li key={b.costType} className="flex justify-between gap-2 p-3"><span>{COST_SECTIONS.find((t) => t.type === b.costType)?.label ?? b.costType}</span><Money value={b.budgetedAmount} /></li>)}
            <li className="flex justify-between gap-2 p-3 font-semibold"><span>Total</span><Money value={budget.data.reduce((a, b) => a + b.budgetedAmount, 0)} /></li>
          </ul>
        ) }] : []),
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Project" id={id} /> },
      ]} />
    </>
  )
}
