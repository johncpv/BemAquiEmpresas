import { Link, useParams } from '@tanstack/react-router'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useGetDashboardProjectResult } from '@/api/generated/bemaqui'
import type { ProjectResultDetail } from '@/api/generated/model'
import { ColumnChart } from '@/components/ui/column-chart'
import { StatusChip } from '@/components/ui/status-chip'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { type TreeColumn, TreeTable } from '@/components/ui/tree-table'
import { Dica } from '@/help/Dica'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import { formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { pct1, shortMoney } from '@/pages/projects/margins/margins'
import { AccessDenied } from '@/pages/ModulePage'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { monthLabel, monthLong, RESULT_DICAS, RESULTS_ROUTE, resultPoints, resultSummary } from '../owner'
import { ResultCellPanel, type ResultCellTarget } from './ResultCellPanel'
import { type LineNode, toTree, useResultFilters } from './results'

const money = (v: number) => formatMoney(v)

function Table({ data, mobile, monthIndex, onOpen }: { data: ProjectResultDetail; mobile: boolean; monthIndex: number; onOpen: (t: ResultCellTarget) => void }) {
  const nodes = useMemo(() => toTree(data.lines), [data.lines])
  const months = data.months
  const columns: TreeColumn<LineNode>[] = mobile
    ? [
        { key: 'mes', header: months[monthIndex] ? monthLabel(months[monthIndex].month) : 'Mês', title: months[monthIndex] ? monthLong(months[monthIndex].month) : 'Mês', numeric: true, cell: (n) => money(n.line.values[monthIndex] ?? 0) },
        { key: 'total', header: 'Total', numeric: true, cell: (n) => money(n.line.total) },
      ]
    : [
        ...months.map((m, i): TreeColumn<LineNode> => ({
          key: m.month, title: `${monthLong(m.month)}${m.provisional ? ', receita provisória' : ''}`, header: monthLabel(m.month), numeric: true, cell: (n) => money(n.line.values[i] ?? 0),
        })),
        { key: 'total', header: 'Total', numeric: true, cell: (n) => money(n.line.total) },
      ]
  const monthOf = (c: number) => (mobile ? (c === 0 ? months[monthIndex]?.month ?? null : null) : c < months.length ? months[c].month : null)
  const valueOf = (n: LineNode, c: number) => (mobile ? (c === 0 ? n.line.values[monthIndex] : n.line.total) : c < months.length ? n.line.values[c] : n.line.total)
  return (
    <TreeTable<LineNode>
      label={`Resultado de ${data.number} mês a mês`}
      nodes={nodes}
      columns={columns}
      labelHeader="Linha"
      searchable={false}
      defaultExpanded="all"
      cellNavigation
      stickyLabel
      renderLabel={(n) => (
        <span className={cn('inline-flex flex-wrap items-center gap-1', n.line.kind !== 'Cost' && 'font-semibold')}>
          {n.label}
          {n.line.key === 'revenue' ? <Dica nome="Receita" texto={RESULT_DICAS.receita} /> : null}
          {n.line.key === 'costs' ? <Dica nome="Custos diretos" texto={RESULT_DICAS.custosDiretos} /> : null}
        </span>
      )}
      canActivate={(n) => n.line.drillable}
      onActivateCell={(n, c) => onOpen({ line: n.line.key, label: n.line.label.replace(/^\(−\) /, ''), month: monthOf(c), value: valueOf(n, c) ?? 0 })}
      rowClassName={(n) => (n.line.kind === 'Result' ? 'bg-muted/60' : undefined)}
      cellClassName={(n, c) => cn(n.line.kind === 'Result' && 'font-semibold', n.line.kind === 'Result' && (valueOf(n, c) ?? 0) < 0 && 'text-crit-ink')}
    />
  )
}

/**
 * Resultado de um projeto mês a mês (DSH-01b CA 7 e 8): `TreeTable` analítica (coluna fixa e navegação por célula) com Receita, (−) custos por
 * natureza e = Resultado, a coluna Total e o `ColumnChart` "Receita × custos diretos por mês" com a referência zero. A 400 px, um mês por vez.
 */
export function ProjectResultPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const allowed = canCost && canFinance
  const [, , period] = useResultFilters()
  const mobile = useMediaQuery(MOBILE)
  const query = useGetDashboardProjectResult(id, { from: period.from, to: period.to }, { query: { enabled: allowed } })
  const [cell, setCell] = useState<ResultCellTarget | null>(null)
  const [index, setIndex] = useState<number | null>(null)
  if (!allowed) return <AccessDenied module={{ name: 'Resultado por projeto (custo, margem e financeiro do projeto)' }} />

  const data = query.data
  const monthIndex = index ?? Math.max(0, (data?.months.length ?? 1) - 1)
  const back = <Link to={RESULTS_ROUTE as never} search={{ de: period.from, ate: period.to } as never} className="text-sm font-semibold text-primary hover:underline">Voltar ao resultado por projeto</Link>
  const result = data?.lines.find((l) => l.key === 'result')
  return (
    <>
      <PageHeading title={data ? `${data.number} · ${data.name}` : 'Resultado do projeto'} documentTitle="Resultado do projeto">
        {data ? <p className="mt-1 text-muted-foreground">{data.partyName ? `${data.partyName} · ` : ''}{monthLong(data.from)} a {monthLong(data.to)}. Clique num valor para ver os documentos.</p> : null}
      </PageHeading>
      <div className="mb-3 flex flex-wrap items-center gap-3">{back}</div>
      <p role="note" className="mb-4 flex items-start gap-1 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
        <span>{RESULT_DICAS.note}</span><Dica nome="Por que não fecha com o DRE" texto={RESULT_DICAS.diferenca} />
      </p>
      {query.isPending ? (
        <div aria-busy="true" aria-label="Carregando o resultado do projeto" className="space-y-3"><Skeleton className="h-56" /><Skeleton className="h-64" /></div>
      ) : query.isError || !data ? (
        <ErrorState title="Não foi possível carregar o resultado do projeto." message="Verifique a conexão e tente de novo. Se o projeto foi excluído, volte à lista." onRetry={() => void query.refetch()} />
      ) : data.lines.every((l) => l.total === 0 && l.values.every((v) => v === 0)) ? (
        <EmptyState title="Nenhuma receita ou custo deste projeto no período." description="Escolha outro período na lista do resultado por projeto." action={back} />
      ) : (
        <div className="space-y-4">
          <section aria-label="Receita × custos diretos por mês" className="space-y-2 rounded-md border border-border bg-card p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-base font-semibold">Receita × custos diretos por mês</h2>
              <span className="inline-flex flex-wrap items-center gap-2 text-sm">
                Margem no período: <b className={cn('tabular-nums', (result?.total ?? 0) < 0 && 'text-crit-ink')}>{pct1(data.marginPct)}</b>
                {data.months.some((m) => m.provisional) ? <><StatusChip tone="info">Provisório</StatusChip><Dica nome="Provisório" texto={RESULT_DICAS.provisorio} /></> : null}
                {(result?.total ?? 0) < 0 ? <Link to={'/app/projetos/$id' as never} params={{ id } as never} search={{ aba: 'custos' } as never} className="font-semibold text-primary hover:underline">Ver os custos do projeto</Link> : null}
              </span>
            </div>
            <ColumnChart title="Receita × custos diretos por mês" data={resultPoints(data.months, data.lines)} xKey="mes" xLabel="Mês" format={(v) => formatMoney(v)} labelFormat={shortMoney}
              series={[{ key: 'receita', label: 'Receita', fill: 'outline' }, { key: 'custos', label: 'Custos diretos', fill: 'solid' }]} summary={resultSummary(data.months, data.lines, (v) => formatMoney(v))} height={240} />
          </section>
          {mobile ? (
            <div className="flex items-center justify-between gap-2" role="group" aria-label="Mês da tabela">
              <button type="button" onClick={() => setIndex(Math.max(0, monthIndex - 1))} disabled={monthIndex === 0} aria-label="Mês anterior" className="grid min-h-[44px] min-w-[44px] place-items-center rounded-md border border-border disabled:opacity-50"><ChevronLeft aria-hidden="true" className="size-4" /></button>
              <span className="text-sm font-semibold">{monthLong(data.months[monthIndex].month)}</span>
              <button type="button" onClick={() => setIndex(Math.min(data.months.length - 1, monthIndex + 1))} disabled={monthIndex >= data.months.length - 1} aria-label="Próximo mês" className="grid min-h-[44px] min-w-[44px] place-items-center rounded-md border border-border disabled:opacity-50"><ChevronRight aria-hidden="true" className="size-4" /></button>
            </div>
          ) : null}
          <Table data={data} mobile={mobile} monthIndex={monthIndex} onOpen={setCell} />
        </div>
      )}
      {cell ? <ResultCellPanel projectId={id} from={period.from} to={period.to} target={cell} onClose={() => setCell(null)} /> : null}
    </>
  )
}
