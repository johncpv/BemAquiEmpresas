import { Link } from '@tanstack/react-router'
import { useGetContract, useGetProjectCosts, useGetProjectFinancial } from '@/api/generated/bemaqui'
import type { ProjectDetail, ProjectForecastView } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today } from '../finance/receivables/receivables'
import { ProjectEditForm } from './ProjectEditForm'
import { delayDays, forecastFix, forecastReasonText, pct, PROJECT_DICAS, riskChip, slackText } from './projects'

/** Resumo (COM-05 CA 3, 4): contratado, datas e origem; recebido com finance.view; margens com projects.view_cost; edição com projects.edit. */
export function ProjectSummaryTab({ project, onSaved, onCosts }: { project: ProjectDetail; onSaved: () => Promise<void>; onCosts: () => void }) {
  const canEdit = useCan('projects.edit')
  const canFinance = useCan('finance.view')
  const canCost = useCan('projects.view_cost')
  const contract = useGetContract(project.contractId)
  const financial = useGetProjectFinancial(project.id, { query: { enabled: canFinance } })
  const costs = useGetProjectCosts(project.id, { query: { enabled: canCost } })
  const c = contract.data
  const cost = costs.data
  const gap = cost && typeof cost.projectedMarginPct === 'number' && typeof cost.budgetedMarginPct === 'number' ? cost.budgetedMarginPct - cost.projectedMarginPct : 0
  const forecast = project.forecast
  const late = forecast?.delayDays ?? delayDays(project.promisedDate, project.status, today())
  const atRisk = forecast?.risk === 'AtRisk' && forecast.forecastDate && project.promisedDate
  return (
    <div className="space-y-4">
      {gap > 0 ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Margem projetada {pct(gap)} abaixo da orçada. <button type="button" onClick={onCosts} className="font-semibold underline">Ver custos</button></p> : null}
      {late ? <p role="note" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">Entrega prometida passou há {late} dias.</p> : null}
      {atRisk ? (
        <p role="note" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          Vai atrasar {Math.abs(forecast.slackDays ?? 0)} {Math.abs(forecast.slackDays ?? 0) === 1 ? 'dia' : 'dias'}: previsão {formatDate(forecast.forecastDate!)}, prometido ao cliente {formatDate(project.promisedDate!)}.{' '}
          <Link to={'/app/producao/os' as never} search={{ projeto: project.id } as never} className="font-semibold underline">Ver as OS do projeto</Link>
        </p>
      ) : null}
      <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Contratado</dt><dd>{c ? <Money value={c.currentValue ?? c.totalValue} className="font-semibold" /> : '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entrega prometida</dt><dd className="tabular-nums">{project.promisedDate ? formatDate(project.promisedDate) : '—'}</dd></div>
        <div>
          <dt className="flex items-center text-xs text-muted-foreground">Entrega prevista<Dica nome="Entrega prevista" texto={PROJECT_DICAS.entregaPrevista} /></dt>
          <ForecastValue projectId={project.id} forecast={forecast} />
        </div>
        <div><dt className="text-xs text-muted-foreground">Origem</dt><dd>{c ? <><Link to={'/app/contratos/$id' as never} params={{ id: c.id } as never} className="font-semibold text-primary hover:underline">{c.number}</Link> · <Link to={'/app/orcamentos/$id' as never} params={{ id: c.quoteId } as never} className="text-primary hover:underline">{c.quoteNumber}</Link></> : '—'}</dd></div>
        {canFinance && financial.data ? (
          <>
            <div><dt className="text-xs text-muted-foreground">Recebido</dt><dd><Money value={financial.data.received} /></dd></div>
            <div><dt className="text-xs text-muted-foreground">A receber</dt><dd><Money value={financial.data.toReceive} /></dd></div>
          </>
        ) : null}
        {canCost && cost ? (
          <>
            <div><dt className="text-xs text-muted-foreground">Margem orçada</dt><dd className="tabular-nums">{pct(cost.budgetedMarginPct)}</dd></div>
            <div><dt className="flex items-center text-xs text-muted-foreground">Margem projetada<Dica nome="Margem projetada" texto={PROJECT_DICAS.margemProjetada} /></dt><dd className="tabular-nums">{pct(cost.projectedMarginPct)}</dd></div>
          </>
        ) : null}
      </dl>
      {canEdit ? <ProjectEditForm key={project.version} project={project} onSaved={onSaved} /> : null}
    </div>
  )
}

/**
 * "Entrega prevista" (PLN-01b CA 8): a data do plano com a folga abaixo (a 400 px fica na mesma célula), o chip "Em risco" e, sem data, o
 * motivo com o link que resolve.
 */
export function ForecastValue({ projectId, forecast }: { projectId: string; forecast: ProjectForecastView | null | undefined }) {
  if (!forecast?.forecastDate) {
    const fix = forecastFix(forecast?.reason, forecast?.reasonOrderId, projectId)
    return (
      <dd className="text-warn-ink">
        <span className="block">Sem previsão: {forecastReasonText(forecast?.reason, forecast?.reasonOrderNumber)}</span>
        {fix ? <Link to={fix.to as never} params={fix.params as never} search={fix.search as never} className="text-xs font-semibold text-primary underline">{fix.label}</Link> : null}
      </dd>
    )
  }

  const chip = riskChip(forecast.risk)
  const slack = slackText(forecast.slackDays)
  return (
    <dd className="tabular-nums">
      <span className="flex flex-wrap items-center gap-1">{formatDate(forecast.forecastDate)}{chip?.tone === 'warn' ? <><StatusChip tone="warn">{chip.label}</StatusChip><Dica nome="Em risco" texto={PROJECT_DICAS.emRisco} /></> : null}</span>
      {slack ? <span className={`flex items-center text-xs ${(forecast.slackDays ?? 0) < 0 ? 'font-semibold text-warn-ink' : 'text-muted-foreground'}`}>{slack}<Dica nome="Folga" texto={PROJECT_DICAS.folga} /></span> : null}
      {forecast.installationOnly ? <span className="block text-xs text-muted-foreground">Pelo fim da instalação</span> : null}
    </dd>
  )
}
