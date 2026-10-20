import { Link } from '@tanstack/react-router'
import { useGetContract, useGetProjectCosts, useGetProjectFinancial } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today } from '../finance/receivables/receivables'
import { ProjectEditForm } from './ProjectEditForm'
import { delayDays, pct, PROJECT_DICAS } from './projects'

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
  const late = delayDays(project.promisedDate, project.status, today())
  return (
    <div className="space-y-4">
      {gap > 0 ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Margem projetada {pct(gap)} abaixo da orçada. <button type="button" onClick={onCosts} className="font-semibold underline">Ver custos</button></p> : null}
      {late ? <p role="note" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">Entrega prometida passou há {late} dias.</p> : null}
      <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Contratado</dt><dd>{c ? <Money value={c.totalValue} className="font-semibold" /> : '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entrega prometida</dt><dd className="tabular-nums">{project.promisedDate ? formatDate(project.promisedDate) : '—'}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entrega prevista</dt><dd className="text-muted-foreground">Calculada a partir da Fase 4</dd></div>
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
