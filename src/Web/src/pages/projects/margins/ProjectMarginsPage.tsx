import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { X } from 'lucide-react'
import { useState } from 'react'
import { useGetProjectMarginPortfolio, useListProjectMarginRanking } from '@/api/generated/bemaqui'
import type { GetProjectMarginPortfolioParams, ProjectMarginRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { ColumnChart } from '@/components/ui/column-chart'
import { DashboardCard } from '@/components/ui/dashboard-card'
import { Kpi } from '@/components/ui/kpi'
import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { AccessDenied } from '@/pages/ModulePage'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStage } from '../../contracts/contracts'
import { CostEntriesPanel, type CostCell } from './CostEntriesPanel'
import { CostLinesTable } from './CostLinesTable'
import { costLabel, marginChip, marginContext, MARGIN_DICAS, pct1, pp, shortMoney, worstSummary } from './margins'

const SITUATIONS = [
  { value: '', label: 'Em andamento' },
  { value: 'entregue', label: 'Entregues' },
  { value: 'encerrado', label: 'Encerrados' },
  { value: 'concluido', label: 'Entregues e encerrados' },
  { value: 'cancelado', label: 'Cancelados' },
  { value: 'todos', label: 'Todos' },
]

const FILTER_KEYS = ['situacao', 'cliente', 'clienteNome', 'responsavel', 'responsavelNome', 'assinadoDe', 'assinadoAte', 'abaixo', 'entregueDe', 'entregueAte'] as const
type Filters = Partial<Record<(typeof FILTER_KEYS)[number], string>>

function useFilters(): [Filters, (patch: Filters) => void] {
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters = Object.fromEntries(FILTER_KEYS.map((k) => [k, typeof raw[k] === 'string' || typeof raw[k] === 'boolean' ? String(raw[k]) : undefined])) as Filters
  const set = (patch: Filters) => void navigate({
    to: '.', replace: true,
    search: (prev: Record<string, unknown>) => {
      const next: Record<string, unknown> = { ...prev, ...patch }
      delete next.page
      for (const k of Object.keys(next)) if (next[k] === undefined || next[k] === '') delete next[k]
      return next
    },
  } as never)
  return [filters, set]
}

/**
 * Comercial → Projetos → Orçado × realizado (DSH-02 CA 9 a 13 e 16 a 19): linha de Kpi do consolidado (margens ponderadas pela receita),
 * tabela por natureza com o ColumnChart e lista ranqueada do pior para o melhor desvio, com filtros na URL. Só com projects.view_cost.
 */
export function ProjectMarginsPage() {
  const canCost = useCan('projects.view_cost')
  const [search] = useTableSearch()
  const [filters, setFilters] = useFilters()
  const [cell, setCell] = useState<CostCell | null>(null)
  const params: GetProjectMarginPortfolioParams = {
    situation: filters.situacao || undefined, partyId: filters.cliente || undefined, managerId: filters.responsavel || undefined,
    signedFrom: filters.assinadoDe || undefined, signedTo: filters.assinadoAte || undefined, belowBudget: filters.abaixo === 'true' || undefined,
    deliveredFrom: filters.entregueDe || undefined, deliveredTo: filters.entregueAte || undefined,
  }
  const portfolio = useGetProjectMarginPortfolio(params, { query: { enabled: canCost } })
  const ranking = useListProjectMarginRanking({ ...params, sort: search.sort, page: search.page, pageSize: search.pageSize }, { query: { enabled: canCost } })
  if (!canCost) return <AccessDenied module={{ name: 'Orçado × realizado (custo e margem do projeto)' }} />

  const filtered = Object.entries(filters).some(([k, v]) => v && k !== 'clienteNome' && k !== 'responsavelNome')
  const clear = () => setFilters(Object.fromEntries(FILTER_KEYS.map((k) => [k, undefined])) as Filters)
  const f = portfolio.data?.figures
  const goQuotes = <Link to={'/app/orcamentos' as never} search={{ situacao: 'enviado' } as never} className="inline-flex min-h-[44px] items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground sm:min-h-0">Ver orçamentos enviados</Link>
  const clearButton = <button type="button" onClick={clear} className="inline-flex min-h-[44px] items-center rounded-md border border-border px-3 py-2 text-sm font-semibold sm:min-h-0">Limpar filtros</button>
  const empty = portfolio.data?.projectCount === 0
  const toNature = () => document.getElementById('por-natureza')?.focus()

  const columns: ColumnDef<ProjectMarginRow>[] = [
    {
      id: 'number', header: 'Projeto', meta: { sortField: 'number' }, cell: ({ row: { original: r } }) => (
        <span><Link to={'/app/projetos/$id' as never} params={{ id: r.project.id } as never} search={{ aba: 'custos' } as never} className="font-semibold text-primary hover:underline">{r.project.number}</Link>
          <span className="block text-xs text-muted-foreground">{r.project.name}</span></span>
      ),
    },
    {
      id: 'party', header: 'Cliente', cell: ({ row: { original: r } }) => (
        <button type="button" onClick={() => setFilters({ cliente: r.project.partyId, clienteNome: r.project.partyName })} className="text-left hover:underline" title="Filtrar por este cliente">{r.project.partyName || '—'}</button>
      ),
    },
    { id: 'stage', header: 'Etapa', meta: { secondary: true }, cell: ({ row: { original: r } }) => (projectStage(r.project.status) < 0 ? <StatusChip tone="neutral">Cancelado</StatusChip> : <StageIndicator stages={PROJECT_STAGES} letters={PROJECT_LETTERS} current={projectStage(r.project.status)} />) },
    { id: 'revenue', header: 'Receita', meta: { numeric: true, secondary: true, sortField: 'revenue' }, cell: ({ row: { original: r } }) => <Money value={r.figures.revenue} cents={false} /> },
    { id: 'budgeted', header: 'Orçado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.figures.total.budgeted} cents={false} /> },
    { id: 'realized', header: 'Realizado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.figures.total.realized} cents={false} /> },
    { id: 'projected', header: 'Projetado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.figures.total.projected} cents={false} /> },
    {
      id: 'margin', header: 'Margem orçada → projetada', meta: { numeric: true, sortField: 'projectedMargin' },
      cell: ({ row: { original: r } }) => <span className="tabular-nums">{pct1(r.figures.budgetedMarginPct)} → <b>{pct1(r.figures.projectedMarginPct)}</b></span>,
    },
    {
      id: 'deviation', header: 'Desvio', meta: { numeric: true, sortField: 'deviation' }, cell: ({ row: { original: r } }) => {
        const chip = marginChip(r.figures)
        return (
          <span className="inline-flex flex-col items-end gap-1">
            <span className="tabular-nums">{pp(r.figures.marginDeviationPp)}</span>
            {chip ? (
              <Link to={'/app/projetos/$id' as never} params={{ id: r.project.id } as never} search={{ aba: 'custos', ...(r.worstCostType ? { natureza: r.worstCostType } : {}) } as never}
                aria-label={`${chip.text}: ver custos de ${r.project.number}${r.worstCostType ? ` com ${costLabel(r.worstCostType)} destacado` : ''}`}>
                <StatusChip tone={chip.tone}>{chip.text}</StatusChip>
              </Link>
            ) : null}
          </span>
        )
      },
    },
  ]

  return (
    <>
      <PageHeading title="Quanto cada projeto está rendendo de verdade.">
        <p className="mt-1 text-muted-foreground">Orçado × realizado e margem de cada projeto, do pior para o melhor desvio. Clique num valor para ver de onde ele vem.</p>
      </PageHeading>

      <div className="mb-4 flex flex-wrap items-end gap-3 text-sm" role="group" aria-label="Filtros do relatório">
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">Situação
          <select value={filters.situacao ?? ''} onChange={(e) => setFilters({ situacao: e.target.value || undefined })} className="min-h-[44px] rounded-md border border-input bg-card px-2 py-1 text-sm text-foreground sm:min-h-0">
            {SITUATIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </label>
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">Assinado de
          <input type="date" value={filters.assinadoDe ?? ''} onChange={(e) => setFilters({ assinadoDe: e.target.value || undefined })} className="min-h-[44px] rounded-md border border-input bg-card px-2 py-1 text-sm text-foreground sm:min-h-0" />
        </label>
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">até
          <input type="date" value={filters.assinadoAte ?? ''} onChange={(e) => setFilters({ assinadoAte: e.target.value || undefined })} className="min-h-[44px] rounded-md border border-input bg-card px-2 py-1 text-sm text-foreground sm:min-h-0" />
        </label>
        <label className="inline-flex min-h-[44px] items-center gap-2 sm:min-h-0">
          <input type="checkbox" checked={filters.abaixo === 'true'} onChange={(e) => setFilters({ abaixo: e.target.checked ? 'true' : undefined })} className="size-4 accent-primary" />
          Só com margem abaixo da orçada
        </label>
        {filters.cliente ? (
          <button type="button" onClick={() => setFilters({ cliente: undefined, clienteNome: undefined })} className="inline-flex min-h-[44px] items-center gap-1 rounded-full border border-border px-3 text-xs sm:min-h-0 sm:py-1">
            Cliente: {filters.clienteNome || 'selecionado'} <X aria-hidden="true" className="size-3" /><span className="sr-only">(tirar filtro)</span>
          </button>
        ) : null}
        {filters.entregueDe || filters.entregueAte ? (
          <button type="button" onClick={() => setFilters({ entregueDe: undefined, entregueAte: undefined })} className="inline-flex min-h-[44px] items-center gap-1 rounded-full border border-border px-3 text-xs sm:min-h-0 sm:py-1">
            Entregues {filters.entregueDe ? `de ${formatDate(filters.entregueDe)}` : ''}{filters.entregueAte ? ` até ${formatDate(filters.entregueAte)}` : ''} <X aria-hidden="true" className="size-3" /><span className="sr-only">(tirar filtro)</span>
          </button>
        ) : null}
        {filters.responsavel ? (
          <button type="button" onClick={() => setFilters({ responsavel: undefined, responsavelNome: undefined })} className="inline-flex min-h-[44px] items-center gap-1 rounded-full border border-border px-3 text-xs sm:min-h-0 sm:py-1">
            Responsável: {filters.responsavelNome || 'selecionado'} <X aria-hidden="true" className="size-3" /><span className="sr-only">(tirar filtro)</span>
          </button>
        ) : null}
        {filtered ? <button type="button" onClick={clear} className="min-h-[44px] text-sm font-semibold text-primary sm:min-h-0">Limpar filtros</button> : null}
      </div>

      <div className="space-y-4">
        <DashboardCard title="Consolidado" subtitle="Projetos do filtro, com as margens ponderadas pela receita." howWeCalculate={MARGIN_DICAS.comoCalculamos} updatedAt={portfolio.dataUpdatedAt}
          onOrigin={toNature} query={portfolio}
          empty={{ when: empty, title: filtered ? 'Nenhum projeto para os filtros.' : 'Nenhum projeto em andamento. Os projetos nascem quando um orçamento é aprovado.', action: filtered ? clearButton : goQuotes }}>
          {f ? (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-5">
              <Kpi label="Receita contratada" value={<Money value={f.revenue} cents={false} />} context={`${portfolio.data?.projectCount} projetos`} />
              <Kpi label="Orçado" value={<Money value={f.total.budgeted} cents={false} />} onClick={toNature} actionLabel="ver por natureza" />
              <Kpi label="Realizado" value={<Money value={f.total.realized} cents={false} />} context={`Comprometido ${formatMoney(f.total.committed, false)}`} onClick={toNature} actionLabel="ver por natureza" />
              <Kpi label="Projetado" value={<Money value={f.total.projected} cents={false} />} context={f.total.deviation > 0 ? `${formatMoney(f.total.deviation, false)} acima do orçado` : 'Dentro do orçado'} onClick={toNature} actionLabel="ver por natureza" />
              <Kpi label="Margem orçada → projetada" value={<span>{pct1(f.budgetedMarginPct)} → {pct1(f.projectedMarginPct)}</span>} context={marginContext(f)}
                meter={typeof f.budgetedMarginPct === 'number' && typeof f.projectedMarginPct === 'number' && f.budgetedMarginPct > 0
                  ? { value: Math.max(0, f.projectedMarginPct), max: f.budgetedMarginPct, label: `Margem projetada de ${pct1(f.projectedMarginPct)} sobre ${pct1(f.budgetedMarginPct)} orçados`, tone: (f.marginDeviationPp ?? 0) < 0 ? 'warn' : 'ok' }
                  : undefined} />
            </div>
          ) : null}
        </DashboardCard>

        {f && !empty ? (
          <section id="por-natureza" tabIndex={-1} aria-label="Por natureza" className="space-y-3 rounded-md border border-border bg-card p-4 outline-none">
            <h2 className="inline-flex items-center text-base font-semibold">Por natureza<Dica nome="Por natureza" texto="Clique em orçado, realizado ou comprometido para ver os projetos que somam o valor." /></h2>
            <ColumnChart title="Orçado × projetado por natureza" xKey="nature" xLabel="Natureza" summary={worstSummary(f)} format={(v) => formatMoney(v)} labelFormat={shortMoney}
              data={f.lines.map((l) => ({ nature: costLabel(l.costType), budgeted: l.budgeted, projected: l.projected }))}
              series={[{ key: 'budgeted', label: 'Orçado', fill: 'outline' }, { key: 'projected', label: 'Projetado', fill: 'solid' }]} />
            <CostLinesTable label="Orçado × realizado por natureza" lines={f.lines} total={f.total} taxBasis={f.taxBasis} highlight={portfolio.data?.worstCostType ?? undefined}
              onCell={(costType, column) => setCell({ scope: 'portfolio', filter: params, costType, column })} />
          </section>
        ) : null}

        {!empty ? (
          <DataTable label="Projetos do pior para o melhor desvio" columns={columns} query={ranking} getRowId={(r) => r.project.id} searchable={false}
            rowClassName={(r) => (marginChip(r.figures)?.tone === 'crit' ? 'border-l-4 border-l-crit' : marginChip(r.figures) ? 'border-l-4 border-l-warn' : undefined)}
            expandable={{
              label: (r) => `Valores de ${r.project.number}`,
              render: (r) => (
                <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
                  <div><dt className="text-xs text-muted-foreground">Receita</dt><dd><Money value={r.figures.revenue} /></dd></div>
                  <div><dt className="text-xs text-muted-foreground">Orçado</dt><dd><Money value={r.figures.total.budgeted} /></dd></div>
                  <div><dt className="text-xs text-muted-foreground">Realizado</dt><dd><Money value={r.figures.total.realized} /></dd></div>
                  <div><dt className="text-xs text-muted-foreground">Projetado</dt><dd><Money value={r.figures.total.projected} /></dd></div>
                  <div className="col-span-2"><dt className="text-xs text-muted-foreground">Responsável</dt><dd>{r.project.managerName ? (
                    <button type="button" onClick={() => setFilters({ responsavel: r.project.managerUserId ?? undefined, responsavelNome: r.project.managerName ?? undefined })} className="text-primary hover:underline">{r.project.managerName}</button>
                  ) : '—'}</dd></div>
                </dl>
              ),
            }}
            empty={{ title: 'Nenhum projeto para mostrar.', action: filtered ? clearButton : goQuotes }} />
        ) : null}
      </div>
      <CostEntriesPanel cell={cell} onClose={() => setCell(null)} />
    </>
  )
}
