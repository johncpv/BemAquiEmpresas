import { useQueryClient } from '@tanstack/react-query'
import { Link, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetProjectCosts } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { budgetLocked } from './budget'
import { BudgetRevisions } from './BudgetRevisions'
import { EditBudgetDialog } from './EditBudgetDialog'
import { CostEntriesPanel, type CostCell } from './margins/CostEntriesPanel'
import { CostLinesTable } from './margins/CostLinesTable'
import { marginChip, MARGIN_DICAS, NO_COST_BUDGET, pct1, pp } from './margins/margins'

/**
 * Custos (COM-05 CA 8, 9; PRD-04 CA 9 e 13; DSH-02 CA 1 a 6, 12, 13 e 15): orçado, realizado, comprometido, projetado e desvio por natureza,
 * pelo mesmo cálculo do relatório "Orçado × realizado"; orçado, realizado e comprometido abrem o drill-down. A natureza em ?natureza= fica
 * destacada (o chip "Margem abaixo" do relatório leva a ela). Só com projects.view_cost.
 * COM-09: com projects.edit_budget, "Editar custo orçado" (no resumo e no aviso "sem orçamento de custo"), menos em projeto encerrado ou
 * cancelado; as edições ficam no fim da aba, com o valor anterior, o novo, o motivo, quem e quando.
 */
export function ProjectCostsTab({ projectId, contractId, project }: { projectId: string; contractId?: string; project?: Pick<ProjectDetail, 'number' | 'version' | 'status'> }) {
  const query = useGetProjectCosts(projectId)
  const client = useQueryClient()
  const canEditBudget = useCan('projects.edit_budget')
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const [cell, setCell] = useState<CostCell | null>(null)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os custos." message="Tente de novo. Se continuar, fale com o administrador." onRetry={() => query.refetch()} />
  const c = query.data
  const chip = marginChip(c)
  const highlight = typeof search.natureza === 'string' ? search.natureza : undefined
  // Tudo o que é lido do projeto (versão, custos, drill-down e edições) é buscado de novo depois de salvar ou de um conflito de versão.
  const refresh = () => client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith(`/api/v1/projects/${projectId}`) })
  const editBudget = project && canEditBudget && !budgetLocked(project.status)
    ? <EditBudgetDialog projectId={projectId} projectNumber={project.number} version={project.version} costs={c} onDone={refresh} onReload={refresh} />
    : null
  return (
    <div className="space-y-3 text-sm">
      {c.noCostBudget ? (
        <p role="note" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-warn-ink">
          Este projeto não tem orçamento de custo: a margem orçada e o desvio não são calculados, e ele fica fora da margem do painel e do Orçado × realizado.
          O custo orçado vem da composição de custo do orçamento aprovado{editBudget ? '; como o contrato já foi aprovado, informe o custo orçado aqui.' : '.'}{' '}
          {contractId ? <Link to={'/app/contratos/$id' as never} params={{ id: contractId } as never} className="inline-flex min-h-[44px] items-center font-semibold underline sm:min-h-0">Ver o contrato e o orçamento de origem</Link> : null}
          {editBudget ? <span className="mt-2 block">{editBudget}</span> : null}
        </p>
      ) : null}
      <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-3 sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Receita contratada</dt><dd><Money value={c.revenue} className="font-semibold" /></dd></div>
        <div><dt className="inline-flex items-center text-xs text-muted-foreground">Margem orçada{c.noCostBudget ? <Dica nome={NO_COST_BUDGET} texto={MARGIN_DICAS.semOrcamento} /> : null}</dt>
          <dd className="tabular-nums">{c.noCostBudget ? <StatusChip tone="warn">{NO_COST_BUDGET}</StatusChip> : pct1(c.budgetedMarginPct)}</dd></div>
        <div><dt className="inline-flex items-center text-xs text-muted-foreground">Margem realizada<Dica nome="Margem realizada" texto="Receita contratada menos o que já foi gasto. Ainda não é o resultado: falta o que vai ser gasto." /></dt><dd><Money value={c.realizedMargin} /></dd></div>
        <div>
          <dt className="inline-flex items-center text-xs text-muted-foreground">Margem projetada<Dica nome="Margem projetada" texto={MARGIN_DICAS.margemProjetada} /></dt>
          {c.noCostBudget
            ? <dd className="tabular-nums"><b>{pct1(c.projectedMarginPct)}</b> <span className="text-xs text-muted-foreground">(só com o custo já lançado)</span></dd>
            : <dd className="tabular-nums">{pct1(c.budgetedMarginPct)} → <b>{pct1(c.projectedMarginPct)}</b> <span className="text-xs text-muted-foreground">({pp(c.marginDeviationPp)}<Dica nome="pp" texto={MARGIN_DICAS.pp} />)</span></dd>}
          {chip ? <dd className="mt-1"><StatusChip tone={chip.tone}>{chip.text}</StatusChip></dd> : null}
        </div>
      </dl>
      {editBudget && !c.noCostBudget ? <div className="flex justify-end">{editBudget}</div> : null}
      {c.lines.every((l) => l.realized === 0 && l.committed === 0) ? <p className="text-muted-foreground">Nenhum custo lançado ainda. O material baixado para as OS, as horas apontadas na produção e os títulos a pagar com o projeto aparecem aqui.</p> : null}
      <CostLinesTable label="Custos por natureza" lines={c.lines} total={c.total} taxBasis={c.taxBasis} highlight={highlight}
        onCell={(costType, column) => setCell({ scope: 'project', projectId, costType, column })} />
      {c.taxGuides > 0 ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-muted-foreground">Guias de imposto são da empresa: entram no DRE, não no projeto.</p> : null}
      {c.nonConformityCount > 0 ? (
        <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-muted-foreground">
          Não conformidades: {c.nonConformityCount}, custo estimado <Money value={c.nonConformityCost} />; o retrabalho já está nas horas e no material.
        </p>
      ) : null}
      <BudgetRevisions projectId={projectId} />
      <CostEntriesPanel cell={cell} onClose={() => setCell(null)} />
    </div>
  )
}
