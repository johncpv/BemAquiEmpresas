import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { X } from 'lucide-react'
import { useListDashboardProjectResults } from '@/api/generated/bemaqui'
import type { ProjectResultRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { Kpi } from '@/components/ui/kpi'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { costLabel, pct1 } from '@/pages/projects/margins/margins'
import { AccessDenied } from '@/pages/ModulePage'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { RESULT_DICAS, RESULTS_ROUTE } from '../owner'
import { useResultFilters } from './results'

const SITUATIONS = [
  { value: '', label: 'Todas' },
  { value: 'andamento', label: 'Em andamento' },
  { value: 'entregue', label: 'Entregues' },
  { value: 'encerrado', label: 'Encerrados' },
  { value: 'cancelado', label: 'Cancelados' },
]

const field = 'min-h-[44px] rounded-md border border-input bg-card px-2 py-1 text-sm text-foreground sm:min-h-0'

/**
 * Visão geral → Resultado por projeto (DSH-01b CA 6; ADR 0028 §1): receita × custos diretos de cada projeto no período, com a nota fixa da
 * diferença com o DRE. Só com projects.view_cost e finance.view. Resultado negativo leva à aba Custos do projeto (propositivo).
 */
export function ProjectResultsPage() {
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const allowed = canCost && canFinance
  const [search] = useTableSearch()
  const [filters, setFilters, period] = useResultFilters()
  const query = useListDashboardProjectResults(
    { situation: filters.situacao || undefined, partyId: filters.cliente || undefined, from: period.from, to: period.to, sort: search.sort, page: search.page, pageSize: search.pageSize },
    { query: { enabled: allowed } },
  )
  if (!allowed) return <AccessDenied module={{ name: 'Resultado por projeto (custo, margem e financeiro do projeto)' }} />

  const filtered = !!(filters.situacao || filters.cliente || filters.de || filters.ate)
  const clear = () => setFilters({ situacao: undefined, cliente: undefined, clienteNome: undefined, de: undefined, ate: undefined })
  const clearButton = <button type="button" onClick={clear} className="inline-flex min-h-[44px] items-center rounded-md border border-border px-3 py-2 text-sm font-semibold sm:min-h-0">Limpar filtros</button>
  const detailSearch = { de: period.from, ate: period.to }
  const t = query.data?.totals

  const columns: ColumnDef<ProjectResultRow>[] = [
    {
      id: 'number', header: 'Projeto', meta: { sortField: 'number' }, cell: ({ row: { original: r } }) => (
        <span>
          <Link to={`${RESULTS_ROUTE}/$id` as never} params={{ id: r.projectId } as never} search={detailSearch as never} className="font-semibold text-primary hover:underline">{r.number}</Link>
          <span className="block max-w-[14rem] truncate text-xs text-muted-foreground">{r.name}</span>
        </span>
      ),
    },
    {
      id: 'party', header: 'Cliente', meta: { secondary: true }, cell: ({ row: { original: r } }) => (
        <button type="button" onClick={() => setFilters({ cliente: r.partyId, clienteNome: r.partyName })} className="text-left hover:underline" title="Filtrar por este cliente">{r.partyName || '—'}</button>
      ),
    },
    { id: 'revenue', header: 'Receita', meta: { numeric: true, sortField: 'revenue' }, cell: ({ row: { original: r } }) => <Money value={r.revenue} cents={false} /> },
    { id: 'costs', header: 'Custos diretos', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => <Money value={r.directCosts} cents={false} /> },
    {
      id: 'result', header: 'Resultado', meta: { numeric: true, sortField: 'result' }, cell: ({ row: { original: r } }) => r.result < 0 ? (
        <Link to={'/app/projetos/$id' as never} params={{ id: r.projectId } as never} search={{ aba: 'custos' } as never} aria-label={`Resultado negativo de ${r.number}: ver os custos`}
          className="font-semibold text-crit-ink hover:underline"><Money value={r.result} cents={false} /></Link>
      ) : <Money value={r.result} cents={false} className="font-semibold" />,
    },
    { id: 'margin', header: 'Margem', meta: { numeric: true, secondary: true, sortField: 'margin' }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{pct1(r.marginPct)}</span> },
  ]

  return (
    <>
      <PageHeading title="Resultado por projeto">
        <p className="mt-1 text-muted-foreground">Receita e custos diretos de cada projeto no período. Abra um projeto para ver mês a mês e clique num valor para ver os documentos.</p>
      </PageHeading>

      <p role="note" className="mb-4 flex items-start gap-1 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
        <span>{RESULT_DICAS.note}</span><Dica nome="Por que não fecha com o DRE" texto={RESULT_DICAS.diferenca} />
      </p>

      <div className="mb-4 flex flex-wrap items-end gap-3 text-sm" role="group" aria-label="Filtros do resultado por projeto">
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">Situação
          <select value={filters.situacao ?? ''} onChange={(e) => setFilters({ situacao: e.target.value || undefined })} className={field}>
            {SITUATIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </label>
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">De
          <input type="month" value={period.from} onChange={(e) => setFilters({ de: e.target.value || undefined })} className={field} />
        </label>
        <label className="inline-flex flex-col gap-1 text-xs font-medium text-muted-foreground">até
          <input type="month" value={period.to} onChange={(e) => setFilters({ ate: e.target.value || undefined })} className={field} />
        </label>
        {filters.cliente ? (
          <button type="button" onClick={() => setFilters({ cliente: undefined, clienteNome: undefined })} className="inline-flex min-h-[44px] items-center gap-1 rounded-full border border-border px-3 text-xs sm:min-h-0 sm:py-1">
            Cliente: {filters.clienteNome || 'selecionado'} <X aria-hidden="true" className="size-3" /><span className="sr-only">(tirar filtro)</span>
          </button>
        ) : null}
        {filtered ? <button type="button" onClick={clear} className="min-h-[44px] text-sm font-semibold text-primary sm:min-h-0">Limpar filtros</button> : null}
      </div>

      {t && t.projects > 0 ? (
        <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Kpi label="Receita" value={<Money value={t.revenue} cents={false} />}
            context={query.data?.provisional ? <span className="inline-flex items-center gap-1"><StatusChip tone="info">Provisório</StatusChip><Dica nome="Provisório" texto={RESULT_DICAS.provisorio} /></span> : `${t.projects} projetos`} />
          <Kpi label="Custos diretos" value={<Money value={t.directCosts} cents={false} />}
            context={<span className="inline-flex items-center">{t.costs.filter((c) => c.amount !== 0).map((c) => costLabel(c.costType)).join(', ') || 'Sem custos'}<Dica nome="Custos diretos" texto={RESULT_DICAS.custosDiretos} /></span>} />
          <Kpi label="Resultado" value={<Money value={t.result} cents={false} highlightNegative />} />
          <Kpi label="Margem" value={pct1(t.marginPct)} />
        </div>
      ) : null}

      <DataTable label="Resultado por projeto, do pior para o melhor" columns={columns} query={query} getRowId={(r) => r.projectId} searchable={false}
        rowClassName={(r) => (r.result < 0 ? 'border-l-4 border-l-crit' : undefined)}
        expandable={{
          label: (r) => `Custos de ${r.number} por natureza`,
          render: (r) => (
            <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-6">
              {r.costs.map((c) => <div key={c.costType}><dt className="text-xs text-muted-foreground">{costLabel(c.costType)}</dt><dd><Money value={c.amount} /></dd></div>)}
              <div className={cn('sm:hidden')}><dt className="text-xs text-muted-foreground">Margem</dt><dd className="tabular-nums">{pct1(r.marginPct)}</dd></div>
            </dl>
          ),
        }}
        empty={{ title: 'Nenhum projeto com receita ou custo no período.', action: filtered ? clearButton : undefined }} />
    </>
  )
}
