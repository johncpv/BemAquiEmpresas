import { Link } from '@tanstack/react-router'
import { Lock } from 'lucide-react'
import { useListProjectCostEntries, useListProjectLaborWorkers, useListProjectMarginEntries } from '@/api/generated/bemaqui'
import type { CostType, GetProjectMarginPortfolioParams, ProjectCostEntries } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { COLUMN_LABEL, costLabel, type CostColumn, ENTRY_KIND } from './margins'

/** Célula aberta no drill-down: de um projeto ou do consolidado (com o filtro da tela). */
export type CostCell =
  | { scope: 'project'; projectId: string; costType: CostType; column: CostColumn }
  | { scope: 'portfolio'; filter: GetProjectMarginPortfolioParams; costType: CostType; column: CostColumn }

const hours = (v: number) => `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 2 })} h`

/** Documento com link só quando a API mandou o link (o usuário tem a permissão do módulo dono, CA 13). */
function Document({ text, link }: { text: string; link?: string | null }) {
  if (!link) return <span>{text}</span>
  const [path, query] = link.split('?')
  return <Link to={path as never} search={(query ? Object.fromEntries(new URLSearchParams(query)) : undefined) as never} className="font-semibold text-primary hover:underline">{text}</Link>
}

function Entries({ data }: { data: ProjectCostEntries }) {
  if (data.entries.length === 0) return <p className="text-sm text-muted-foreground">Nenhum lançamento nesta célula.</p>
  return (
    <ul className="divide-y divide-border text-sm" aria-label="Lançamentos">
      {data.entries.map((e, i) => (
        <li key={i} className="flex items-start justify-between gap-3 py-2">
          <div className="min-w-0">
            <p className="text-xs text-muted-foreground">{ENTRY_KIND[e.kind]}{e.date ? ` · ${formatDate(e.date)}` : ''}{typeof e.hours === 'number' ? ` · ${hours(e.hours)}` : ''}</p>
            <p className="truncate"><Document text={e.document} link={e.link} /></p>
          </div>
          <Money value={e.amount} highlightNegative className="shrink-0" />
        </li>
      ))}
    </ul>
  )
}

/** Horas por funcionário (CA 14): só com hr.view_sensitive; sem ela, o bloco "Dados protegidos" (padrão 8) e nada é pedido ao servidor. */
function LaborWorkers({ projectId }: { projectId: string }) {
  const canSensitive = useCan('hr.view_sensitive')
  const workers = useListProjectLaborWorkers(projectId, { query: { enabled: canSensitive } })
  if (!canSensitive) {
    return (
      <div className="flex items-center gap-2 rounded-md border border-border bg-muted px-3 py-3 text-sm text-muted-foreground">
        <Lock aria-hidden="true" className="size-4" />
        Dados protegidos
        <Dica nome="Dados protegidos" texto="As horas aparecem por OS e centro de trabalho. Nome de quem trabalhou e custo-hora individual só com a permissão Ver dados sensíveis do RH." />
      </div>
    )
  }

  if (workers.isPending) return <Skeleton className="h-16" />
  if (workers.isError) return <ErrorState title="Não foi possível carregar as horas por funcionário." onRetry={() => workers.refetch()} />
  return (
    <section aria-label="Horas por funcionário" className="space-y-1">
      <h3 className="text-sm font-semibold">Por funcionário</h3>
      <ul className="divide-y divide-border text-sm">
        {workers.data.entries.map((w, i) => (
          <li key={i} className="flex items-start justify-between gap-3 py-2">
            <div className="min-w-0">
              <p className="text-xs text-muted-foreground">{formatDate(w.date)} · {hours(w.hours)}{typeof w.hourlyCost === 'number' ? <> · <Money value={w.hourlyCost} />/h</> : null}</p>
              <p className="truncate">{w.workerName || 'Sem nome'} · <Document text={`${w.orderNumber}${w.workCenterName ? ` · ${w.workCenterName}` : ''}`} link={w.link} /></p>
            </div>
            <Money value={w.amount} highlightNegative className="shrink-0" />
          </li>
        ))}
      </ul>
    </section>
  )
}

/**
 * Drill-down de uma célula (DSH-02 CA 13 a 15, padrão 3 das Fases 4 a 8): painel lateral (tela cheia a 400 px) com os lançamentos que
 * somam o valor, o total igual ao da célula e, no comprometido de material, a conta "títulos de compra − baixas" no topo.
 */
export function CostEntriesPanel({ cell, onClose }: { cell: CostCell | null; onClose: () => void }) {
  const project = cell?.scope === 'project' ? cell : null
  const portfolio = cell?.scope === 'portfolio' ? cell : null
  const byProject = useListProjectCostEntries(project?.projectId ?? '', project?.costType ?? '', { column: project?.column }, { query: { enabled: !!project } })
  const byPortfolio = useListProjectMarginEntries({ ...(portfolio?.filter ?? {}), costType: portfolio?.costType ?? 'Material', column: portfolio?.column }, { query: { enabled: !!portfolio } })
  const query = project ? byProject : byPortfolio
  const title = cell ? `${COLUMN_LABEL[cell.column]} de ${costLabel(cell.costType)}${portfolio ? ' (todos os projetos do filtro)' : ''}` : ''
  const data = query.data
  return (
    <SidePanel open={!!cell} onOpenChange={(open) => !open && onClose()} title={title} description="Os lançamentos que somam o valor da célula, com o documento de origem.">
      {query.isPending ? <Skeleton className="h-40" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar os lançamentos." onRetry={() => query.refetch()} />
      ) : data ? (
        <div className="space-y-4">
          {typeof data.purchasedTotal === 'number' && typeof data.issuedTotal === 'number' ? (
            <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">
              Títulos de compra <Money value={data.purchasedTotal} /> − baixas <Money value={data.issuedTotal} /> = <b><Money value={Math.max(0, data.purchasedTotal - data.issuedTotal)} /></b>
              {data.issuedTotal > data.purchasedTotal ? <span className="block text-xs text-muted-foreground">A baixa passou da compra (material de estoque anterior): o comprometido fica em zero, sem crédito.</span> : null}
            </p>
          ) : null}
          <Entries data={data} />
          <p className="flex items-center justify-between border-t border-border pt-2 font-semibold"><span>Total</span><Money value={data.total} /></p>
          {project && project.costType === 'Labor' && project.column === 'realized' ? <LaborWorkers projectId={project.projectId} /> : null}
        </div>
      ) : null}
    </SidePanel>
  )
}
