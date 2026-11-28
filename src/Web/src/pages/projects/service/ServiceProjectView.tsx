import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getDownloadProjectSummaryPdfUrl, useGetContract, useGetProjectMilestonePreview } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { DocumentActions } from '@/components/ui/document-actions'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProjectBillingPanel } from '../ProjectBillingPanel'
import { ProjectCostsTab } from '../ProjectCostsTab'
import { ProjectDocumentsTab } from '../ProjectDocumentsTab'
import { ProjectFinancialTab } from '../ProjectFinancialTab'
import { ProjectItemsTab } from '../ProjectItemsTab'
import { ProjectStageIndicator } from '../ProjectStageIndicator'
import { ProjectSummaryTab } from '../ProjectSummaryTab'
import { ProjectTimelineTab } from '../ProjectTimelineTab'
import { nextStep, SERVICE_DICAS, type ServiceStep, STEP_VERBS } from './service'
import { ServiceStepDialog } from './ServiceStepDialog'

/** Ajuda própria do projeto de serviço (EXE-01b CA 19): Resumo e Itens; as demais abas usam a ajuda de sempre. */
const SERVICE_HELP_TABS = { resumo: 'servico', itens: 'servico-itens' } as const

const primaryClass = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondaryClass = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Projeto de serviço (EXE-01b CA 1 a 6, 17 e 18): chip "Serviço", indicador P · I · E · A · C, um botão primário por situação com a linha de
 * contexto, "Mais ações" com a entrega direta e o desfazer aceite, e só as abas que fazem sentido (sem Cronograma; "Itens" sem OS). A 400 px,
 * o próximo passo fica fixo no rodapé.
 */
export function ServiceProjectView({ project: p, refresh }: { project: ProjectDetail; refresh: () => Promise<void> }) {
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { acao?: string }
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const canEdit = useCan('projects.edit')
  const canApprove = useCan('projects.approve')
  const canAccept = useCan('projects.register_acceptance')
  const canBill = useCan('fiscal.create')
  const perms: Record<string, boolean> = { 'projects.edit': canEdit, 'projects.approve': canApprove, 'projects.register_acceptance': canAccept }
  const delivered = p.status === 'Delivered' && !p.acceptance && !p.acceptedOn
  const acceptancePreview = useGetProjectMilestonePreview(p.id, { step: 'acceptance' }, { query: { enabled: delivered && p.serviceExecution !== false } })
  const contract = useGetContract(p.contractId, { query: { enabled: p.status === 'Closed' } })
  const step = nextStep(p, (perm) => perms[perm] ?? false, { acceptancePreview: acceptancePreview.data, closedOn: contract.data?.completedAt?.slice(0, 10) ?? null })
  const [chosen, setChosen] = useState<ServiceStep | null>(null)
  const [autoClosed, setAutoClosed] = useState(false)
  const active = p.contractStatus === 'Active'
  // Pauta (CA 16): "Registrar aceite do cliente" leva a ?acao=aceite e o diálogo abre direto (uma vez).
  const dialog = chosen ?? (search.acao === 'aceite' && step.primary === 'acceptance' && !autoClosed ? 'acceptance' : null)
  const setDialog = (next: ServiceStep | null) => {
    setChosen(next)
    if (next === null) setAutoClosed(true)
  }

  const primary = step.primary ? (
    <button type="button" onClick={() => setDialog(step.primary)} className={primaryClass}>{STEP_VERBS[step.primary].verb}</button>
  ) : null
  const more = step.more.length > 0 || (canEdit && active) ? (
    <details className="relative">
      <summary className={`${secondaryClass} cursor-pointer list-none`}>Mais ações</summary>
      <div className="absolute right-0 z-10 mt-1 flex min-w-56 flex-col gap-1 rounded-md border border-border bg-card p-2 shadow">
        {step.more.map((s) => (
          <button key={s} type="button" onClick={(e) => { (e.currentTarget.closest('details') as HTMLDetailsElement | null)?.removeAttribute('open'); setDialog(s) }} className="rounded-md px-3 py-2 text-left text-sm font-semibold hover:bg-muted">
            {STEP_VERBS[s].verb}
          </button>
        ))}
        {canEdit && active ? <Link to={'/app/contratos/$id/aditivos/$aditivoId' as never} params={{ id: p.contractId, aditivoId: 'novo' } as never} className="rounded-md px-3 py-2 text-sm font-semibold hover:bg-muted sm:hidden">Aditivo</Link> : null}
        <Link to={'/app/contratos/$id' as never} params={{ id: p.contractId } as never} className="rounded-md px-3 py-2 text-sm font-semibold hover:bg-muted sm:hidden">Ver contrato {p.contractNumber}</Link>
      </div>
    </details>
  ) : null

  return (
    <>
      <PageHeading title={`${p.number} — ${p.name}`}
        meta={
          <>
            {p.isOnboarding ? <Tag>Implantação</Tag> : null}
            <Tag>Serviço</Tag>
            <span className="inline-flex items-center">
              <ProjectStageIndicator project={p} showLabel />
              <Dica nome="Etapas do serviço" texto={SERVICE_DICAS.legenda} />
            </span>
            {step.awaitingAcceptance ? (
              <span className="inline-flex items-center"><StatusChip tone="warn">Aguardando aceite do cliente</StatusChip><Dica nome="Aguardando aceite do cliente" texto={SERVICE_DICAS.aguardando} /></span>
            ) : null}
            <span className="text-sm text-muted-foreground">{p.partyName}</span>
          </>
        }
        actions={
          <>
            <DocumentActions url={getDownloadProjectSummaryPdfUrl(p.id)} fileName={`${p.number}.pdf`} />
            <Link to={'/app/contratos/$id' as never} params={{ id: p.contractId } as never} className={`${secondaryClass} hidden sm:inline-flex`}>Ver contrato {p.contractNumber}</Link>
            {canEdit && active ? <Link to={'/app/contratos/$id/aditivos/$aditivoId' as never} params={{ id: p.contractId, aditivoId: 'novo' } as never} className={`${secondaryClass} hidden sm:inline-flex`}>Aditivo</Link> : null}
            {canBill && p.status !== 'Cancelled' ? <ProjectBillingPanel projectId={p.id} /> : null}
            {more}
            {primary ? <span className="hidden sm:inline-flex">{primary}</span> : null}
          </>
        } />
      <p className="mb-3 text-sm text-muted-foreground" data-testid="proximo-passo">{step.context}</p>
      {p.acceptance && p.acceptance.kind === 'Inherited' ? <p className="mb-3 text-sm text-muted-foreground">Aceite herdado da entrega em {formatDate(p.acceptance.occurredOn)} (ajuste automático).</p> : null}
      <UrlTabs label="Seções do projeto" fallback="resumo" helpTabs={SERVICE_HELP_TABS} tabs={[
        { id: 'resumo', label: 'Resumo', content: <ProjectSummaryTab project={p} onSaved={refresh} onCosts={() => void navigate({ to: '/app/projetos/$id', params: { id: p.id }, search: { aba: 'custos' } } as never)} /> },
        { id: 'itens', label: 'Itens', content: <ProjectItemsTab project={p} /> },
        ...(canFinance ? [{ id: 'financeiro', label: 'Financeiro', content: <ProjectFinancialTab projectId={p.id} /> }] : []),
        ...(canCost ? [{ id: 'custos', label: 'Custos', content: <ProjectCostsTab projectId={p.id} contractId={p.contractId} project={p} /> }] : []),
        { id: 'documentos', label: 'Documentos', content: <ProjectDocumentsTab projectId={p.id} contractId={p.contractId} /> },
        { id: 'linha-do-tempo', label: 'Linha do tempo', content: <ProjectTimelineTab projectId={p.id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Project" id={p.id} /> },
      ]} />
      {/* CA 18: a 400 px, o próximo passo fica fixo no rodapé (acima do dock). */}
      {primary ? <div className="fixed inset-x-0 bottom-16 z-20 border-t border-border bg-background p-3 sm:hidden [&>button]:min-h-11 [&>button]:w-full">{primary}</div> : null}
      {primary ? <div aria-hidden="true" className="h-16 sm:hidden" /> : null}
      {dialog ? <ServiceStepDialog project={p} step={dialog} open onOpenChange={(o) => { if (!o) setDialog(null) }} onDone={refresh} onReload={refresh} /> : null}
    </>
  )
}
