import { useGetProjectCosts } from '@/api/generated/bemaqui'
import type { ProjectCostLine } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { COST_SECTIONS } from '../quotes/quotes'
import { pct, PROJECT_DICAS } from './projects'

const label = (l: ProjectCostLine) => COST_SECTIONS.find((s) => s.type === l.costType)?.label ?? l.costType

function Deviation({ line }: { line: ProjectCostLine }) {
  const up = line.deviation > 0
  return <span className={up ? 'tabular-nums text-crit-ink' : 'tabular-nums'}>{up ? '+' : ''}<Money value={line.deviation} /> ({up ? '+' : ''}{pct(line.deviationPct)})</span>
}

/** Marca "Calculado na implantação" (padrão 11 da Fase 3, PRD-04 CA 13) no realizado recomposto. */
function Backfill({ line }: { line: ProjectCostLine }) {
  if (!line.includesBackfill) return null
  return <span className="ml-1 inline-flex items-center align-middle"><Tag>Calculado na implantação</Tag><Dica nome="Calculado na implantação" texto={PROJECT_DICAS.implantacao} /></span>
}

/**
 * Custos (COM-05 CA 8, 9; PRD-04 CA 9 e 13): orçado, realizado, comprometido, projetado e desvio por natureza. A 400 px, cartões
 * com orçado, realizado e projetado; comprometido e desvio na linha expansível. Só com projects.view_cost.
 */
export function ProjectCostsTab({ projectId }: { projectId: string }) {
  const query = useGetProjectCosts(projectId)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os custos." message="Tente de novo. Se continuar, fale com o administrador." onRetry={() => query.refetch()} />
  const c = query.data
  const rows = [...c.lines.map((l) => ({ name: label(l), line: l })), { name: 'Total', line: c.total }]
  return (
    <div className="space-y-3 text-sm">
      {c.lines.every((l) => l.realized === 0 && l.committed === 0) ? <p className="text-muted-foreground">Nenhum custo lançado ainda. O material baixado para as OS, as horas apontadas na produção e os títulos a pagar com o projeto aparecem aqui.</p> : null}
      <table className="hidden w-full sm:table" aria-label="Custos por natureza">
        <thead className="text-xs text-muted-foreground">
          <tr>
            <th className="py-1 text-left">Natureza</th>
            <th className="py-1 text-right">Orçado</th>
            <th className="py-1 text-right"><span className="inline-flex items-center">Realizado<Dica nome="Realizado" texto={PROJECT_DICAS.realizado} /></span></th>
            <th className="py-1 text-right"><span className="inline-flex items-center">Comprometido<Dica nome="Comprometido" texto={PROJECT_DICAS.comprometido} /></span></th>
            <th className="py-1 text-right"><span className="inline-flex items-center">Projetado<Dica nome="Projetado" texto={PROJECT_DICAS.projetado} /></span></th>
            <th className="py-1 text-right"><span className="inline-flex items-center">Desvio<Dica nome="Desvio" texto={PROJECT_DICAS.desvio} /></span></th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ name, line }) => (
            <tr key={name} className={name === 'Total' ? 'border-t border-border font-semibold' : 'border-t border-border'}>
              <td className="py-1.5">{name}{name !== 'Total' ? <Backfill line={line} /> : null}</td>
              <td className="text-right"><Money value={line.budgeted} /></td>
              <td className="text-right"><Money value={line.realized} /></td>
              <td className="text-right"><Money value={line.committed} /></td>
              <td className="text-right"><Money value={line.projected} /></td>
              <td className="text-right"><Deviation line={line} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      <ul className="space-y-2 sm:hidden" aria-label="Custos por natureza em cartões">
        {rows.map(({ name, line }) => (
          <li key={name} className="rounded-md border border-border bg-card p-3">
            <b>{name}</b>{name !== 'Total' ? <Backfill line={line} /> : null}
            <p>Orçado <Money value={line.budgeted} /> · Realizado <Money value={line.realized} /></p>
            <p>Projetado <Money value={line.projected} /></p>
            <details className="mt-1">
              <summary className="cursor-pointer text-xs font-semibold text-primary">Comprometido e desvio</summary>
              <p>Comprometido <Money value={line.committed} /></p>
              <p>Desvio <Deviation line={line} /></p>
            </details>
          </li>
        ))}
      </ul>
    </div>
  )
}