import { useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetProjectCosts } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { CostEntriesPanel, type CostCell } from './margins/CostEntriesPanel'
import { CostLinesTable } from './margins/CostLinesTable'
import { marginChip, MARGIN_DICAS, pct1, pp } from './margins/margins'

/**
 * Custos (COM-05 CA 8, 9; PRD-04 CA 9 e 13; DSH-02 CA 1 a 6, 12, 13 e 15): orçado, realizado, comprometido, projetado e desvio por natureza,
 * pelo mesmo cálculo do relatório "Orçado × realizado"; orçado, realizado e comprometido abrem o drill-down. A natureza em ?natureza= fica
 * destacada (o chip "Margem abaixo" do relatório leva a ela). Só com projects.view_cost.
 */
export function ProjectCostsTab({ projectId }: { projectId: string }) {
  const query = useGetProjectCosts(projectId)
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const [cell, setCell] = useState<CostCell | null>(null)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os custos." message="Tente de novo. Se continuar, fale com o administrador." onRetry={() => query.refetch()} />
  const c = query.data
  const chip = marginChip(c)
  const highlight = typeof search.natureza === 'string' ? search.natureza : undefined
  return (
    <div className="space-y-3 text-sm">
      <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-3 sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Receita contratada</dt><dd><Money value={c.revenue} className="font-semibold" /></dd></div>
        <div><dt className="text-xs text-muted-foreground">Margem orçada</dt><dd className="tabular-nums">{pct1(c.budgetedMarginPct)}</dd></div>
        <div><dt className="inline-flex items-center text-xs text-muted-foreground">Margem realizada<Dica nome="Margem realizada" texto="Receita contratada menos o que já foi gasto. Ainda não é o resultado: falta o que vai ser gasto." /></dt><dd><Money value={c.realizedMargin} /></dd></div>
        <div>
          <dt className="inline-flex items-center text-xs text-muted-foreground">Margem projetada<Dica nome="Margem projetada" texto={MARGIN_DICAS.margemProjetada} /></dt>
          <dd className="tabular-nums">{pct1(c.budgetedMarginPct)} → <b>{pct1(c.projectedMarginPct)}</b> <span className="text-xs text-muted-foreground">({pp(c.marginDeviationPp)}<Dica nome="pp" texto={MARGIN_DICAS.pp} />)</span></dd>
          {chip ? <dd className="mt-1"><StatusChip tone={chip.tone}>{chip.text}</StatusChip></dd> : null}
        </div>
      </dl>
      {c.lines.every((l) => l.realized === 0 && l.committed === 0) ? <p className="text-muted-foreground">Nenhum custo lançado ainda. O material baixado para as OS, as horas apontadas na produção e os títulos a pagar com o projeto aparecem aqui.</p> : null}
      <CostLinesTable label="Custos por natureza" lines={c.lines} total={c.total} taxBasis={c.taxBasis} highlight={highlight}
        onCell={(costType, column) => setCell({ scope: 'project', projectId, costType, column })} />
      {c.taxGuides > 0 ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-muted-foreground">Guias de imposto são da empresa: entram no DRE, não no projeto.</p> : null}
      {c.nonConformityCount > 0 ? (
        <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-muted-foreground">
          Não conformidades: {c.nonConformityCount}, custo estimado <Money value={c.nonConformityCost} />; o retrabalho já está nas horas e no material.
        </p>
      ) : null}
      <CostEntriesPanel cell={cell} onClose={() => setCell(null)} />
    </div>
  )
}
