import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { getGetContractQueryKey, getGetProjectQueryKey, useGetProject } from '@/api/generated/bemaqui'
import { RecordHistory } from '@/components/history/RecordHistory'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStage } from '../contracts/contracts'
import { ProjectCostsTab } from './ProjectCostsTab'
import { ProjectDocumentsTab } from './ProjectDocumentsTab'
import { ProjectEventDialog } from './ProjectEventDialog'
import { ProjectFinancialTab } from './ProjectFinancialTab'
import { ProjectItemsTab } from './ProjectItemsTab'
import { ProjectSummaryTab } from './ProjectSummaryTab'
import { ProjectTimelineTab } from './ProjectTimelineTab'

/** Projeto 360 (COM-05): cabeçalho com a etapa e as ações; abas na URL; aba sem permissão some. */
export function ProjectPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const navigate = useNavigate()
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const canEdit = useCan('projects.edit')
  const canApprove = useCan('projects.approve')
  const client = useQueryClient()
  const query = useGetProject(id)
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
          <span className="text-sm text-muted-foreground">{p.partyName}</span>
          <Link to={'/app/contratos/$id' as never} params={{ id: p.contractId } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver contrato {p.contractNumber}</Link>
          {canFabricate ? <ProjectEventDialog project={p} kind="fabrication" onDone={refresh} /> : null}
          {canDeliver ? <ProjectEventDialog project={p} kind="delivery" onDone={refresh} /> : null}
        </div>
      </PageHeading>
      {p.fabricatedOn || p.deliveredOn ? (
        <p className="mb-3 text-sm text-muted-foreground">
          {p.fabricatedOn ? `Fabricação concluída em ${formatDate(p.fabricatedOn)}. ` : ''}
          {p.deliveredOn ? `Entregue em ${formatDate(p.deliveredOn)}${p.deliveredManually ? ' (registrada à mão)' : ''}.` : ''}
        </p>
      ) : null}
      <UrlTabs label="Seções do projeto" fallback="resumo" tabs={[
        { id: 'resumo', label: 'Resumo', content: <ProjectSummaryTab project={p} onSaved={refresh} onCosts={() => void navigate({ to: '/app/projetos/$id', params: { id }, search: { aba: 'custos' } } as never)} /> },
        { id: 'itens', label: 'Itens e OS', content: <ProjectItemsTab project={p} /> },
        ...(canFinance ? [{ id: 'financeiro', label: 'Financeiro', content: <ProjectFinancialTab projectId={id} /> }] : []),
        ...(canCost ? [{ id: 'custos', label: 'Custos', content: <ProjectCostsTab projectId={id} /> }] : []),
        { id: 'documentos', label: 'Documentos', content: <ProjectDocumentsTab projectId={id} contractId={p.contractId} /> },
        { id: 'linha-do-tempo', label: 'Linha do tempo', content: <ProjectTimelineTab projectId={id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Project" id={id} /> },
      ]} />
    </>
  )
}
