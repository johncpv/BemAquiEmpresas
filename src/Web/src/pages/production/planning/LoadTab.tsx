import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useGetFactoryLoad, useGetFactoryLoadCell } from '@/api/generated/bemaqui'
import type { FactoryLoadRow, FactoryLoadView, FactoryLoadWeek } from '@/api/generated/model'
import { Chart } from '@/components/ui/chart'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import {
  afterRows, bandChip, chartData, filtersToSearch, horizonEnd, hoursOf, isEmptyPlan, LOAD_DICAS, type LoadFilters, overCount, parseFilters, percentText, planLine, shortDate,
  summaryLine, WEEK_OPTIONS, weekLabel,
} from './load'
import { OccupancyGrid } from './OccupancyGrid'
import { forecastText, hoursText } from './planning'

const primary = 'inline-flex min-h-[44px] items-center justify-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const action = 'inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'
const select = 'min-h-[44px] w-full rounded-md border border-input bg-background px-2 text-sm sm:w-auto'
const iconButton = 'inline-flex size-11 shrink-0 items-center justify-center rounded-md border border-border bg-card disabled:opacity-40'

interface Props {
  canEdit: boolean
  recalculating: boolean
  onRecalculate: () => Promise<void>
}

/**
 * Planejamento → Ocupação (PLN-04): horas comprometidas pelo plano ÷ capacidade, por semana e por centro, com a linha "Fábrica" (CA 1 e 2);
 * o quadro "Depois de {data}" (CA 3); "Acima da capacidade" com "Recalcular agora" (CA 4); o drill-down da célula (CA 5); as faixas (CA 6);
 * a tabela com coluna fixa e navegação por célula (CA 7); o gráfico com a referência "Capacidade" (CA 8) e os filtros na URL (CA 9). A 400 px,
 * uma semana por vez com os centros em lista.
 */
export function LoadTab({ canEdit, recalculating, onRecalculate }: Props) {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters = parseFilters(search)
  const setFilters = (next: Partial<LoadFilters>) =>
    void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...filtersToSearch({ ...filters, ...next }) }) } as never)
  const query = useGetFactoryLoad({ weeks: filters.weeks, line: filters.line })

  if (query.isPending) return <Skeleton className="h-64" />
  if (query.isError) {
    return <ErrorState title="Não foi possível carregar a ocupação." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
  }

  if (query.data.activeCenters === 0) {
    return (
      <EmptyState title="Cadastre os centros de trabalho para ver a ocupação." description="A ocupação compara as horas planejadas com as horas que cada centro tem por dia."
        action={<Link to={'/app/producao/centros-de-trabalho' as never} className={action}>Centros de trabalho</Link>} />
    )
  }

  return <LoadContent data={query.data} filters={filters} setFilters={setFilters} canEdit={canEdit} recalculating={recalculating} onRecalculate={onRecalculate} />
}

function LoadContent({ data, filters, setFilters, canEdit, recalculating, onRecalculate }: Props & {
  data: FactoryLoadView
  filters: LoadFilters
  setFilters: (next: Partial<LoadFilters>) => void
}) {
  const { load } = data
  const [open, setOpen] = useState<{ row: FactoryLoadRow; week: number } | null>(null)
  const centers = useMemo(() => load.centers.filter((c) => !filters.center || c.workCenterId === filters.center), [load.centers, filters.center])
  const total = useMemo(() => (filters.center ? null : { ...load.factory, name: filters.line ? `Total de ${filters.line}` : 'Fábrica' }), [load.factory, filters.center, filters.line])
  const rows = total ? [...centers, total] : centers
  const chartRow = centers.length === 1 && filters.center ? centers[0] : total ?? load.factory
  const end = horizonEnd(load)
  const overs = overCount(rows)
  const empty = isEmptyPlan(load)
  const weekIndex = Math.max(0, load.weeks.findIndex((w) => w.start === filters.week))

  const filterBar = (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
        <span className="font-semibold">Linha</span>
        <select className={select} value={filters.line ?? ''} onChange={(e) => setFilters({ line: e.target.value || undefined, center: undefined })}>
          <option value="">Todas as linhas</option>
          {data.lines.map((l) => <option key={l} value={l}>{l}</option>)}
        </select>
      </label>
      <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
        <span className="font-semibold">Centro</span>
        <select className={select} value={filters.center ?? ''} onChange={(e) => setFilters({ center: e.target.value || undefined })}>
          <option value="">Todos os centros</option>
          {load.centers.map((c) => <option key={c.workCenterId!} value={c.workCenterId!}>{c.name}</option>)}
        </select>
      </label>
      <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
        <span className="font-semibold">Semanas</span>
        <select className={select} value={filters.weeks} onChange={(e) => setFilters({ weeks: Number(e.target.value), week: undefined })}>
          {[...new Set([...WEEK_OPTIONS, filters.weeks])].sort((a, b) => a - b).map((n) => <option key={n} value={n}>{n} semanas</option>)}
        </select>
      </label>
      <div role="group" aria-label="Visão" className="flex gap-1">
        {(['tabela', 'grafico'] as const).map((v) => (
          <button key={v} type="button" aria-pressed={filters.view === v} onClick={() => setFilters({ view: v })}
            className={cn('inline-flex min-h-[44px] items-center rounded-md border border-border px-4 text-sm font-semibold', filters.view === v ? 'bg-primary text-primary-foreground' : 'bg-card')}>
            {v === 'tabela' ? 'Tabela' : 'Gráfico'}
          </button>
        ))}
      </div>
    </div>
  )

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        <span className="font-semibold text-foreground tabular-nums">{planLine(load.planComputedAt, load.today)}.</span>{' '}
        Horas <span className="whitespace-nowrap">comprometidas<Dica nome="Comprometidas" texto={LOAD_DICAS.comprometidas} /></span> pelo plano sobre a{' '}
        <span className="whitespace-nowrap">capacidade<Dica nome="Capacidade" texto={LOAD_DICAS.capacidade} /></span> de cada centro, por semana (segunda a domingo; a semana atual conta a partir de hoje).
      </p>
      {filterBar}

      {overs > 0 ? (
        <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <span>
            <strong>Acima da capacidade</strong><Dica nome="Acima da capacidade" texto={LOAD_DICAS.acima} />{' '}
            {overs === 1 ? 'em 1 semana' : `em ${overs} semanas`}: o calendário mudou depois do último cálculo do plano.
          </span>
          {canEdit ? <button type="button" className={primary} disabled={recalculating} onClick={() => void onRecalculate()}>{recalculating ? 'Recalculando…' : 'Recalcular agora'}</button> : null}
        </div>
      ) : null}

      {empty ? (
        <EmptyState title="Nenhuma etapa planejada no período." description="Quando o plano datar as etapas das OS de fabricação, as horas aparecem aqui por semana e centro."
          action={<Link to={'/app/producao/planejamento' as never} search={{ aba: 'fila' } as never} className={action}>Ver a fila</Link>} />
      ) : centers.length === 0 ? (
        <EmptyState title="Nenhum centro com esse filtro." description="Escolha outra linha ou outro centro."
          action={<button type="button" className={action} onClick={() => setFilters({ line: undefined, center: undefined })}>Limpar filtros</button>} />
      ) : filters.view === 'grafico' ? (
        <Chart title={chartTitle(chartRow)} data={chartData(load, chartRow)} xKey="semana" xLabel="Semana"
          summary={<span className="tabular-nums" data-testid="ocupacao-resumo">{summaryLine(load, chartRow, chartRow.workCenterId ? [chartRow] : centers)}</span>}
          series={[{ key: 'ocupacao', label: 'Ocupação' }]} format={(v) => percentText(v)}
          yReferences={[{ y: 100, label: 'Capacidade (100%)', extend: true, legend: true }]} valueLabels />
      ) : (
        <>
          <div className="hidden md:block">
            <OccupancyGrid label="Ocupação por semana e centro" weeks={load.weeks} rows={rows} onOpen={(row, week) => setOpen({ row, week })} />
            <p className="mt-2 flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
              <span>Setas movem entre as células; Enter abre as etapas da semana.</span>
              <span className="inline-flex items-center"><StatusChip tone="info">Folga</StatusChip>&nbsp;abaixo de 60%</span>
              <span className="inline-flex items-center"><StatusChip tone="warn">Lotado</StatusChip><Dica nome="Lotado" texto={LOAD_DICAS.lotado} />&nbsp;95% ou mais</span>
            </p>
          </div>
          <MobileWeek weeks={load.weeks} index={weekIndex} rows={rows} onWeek={(i) => setFilters({ week: load.weeks[i].start })} onOpen={(row) => setOpen({ row, week: weekIndex })} />
        </>
      )}

      {!empty && end ? <AfterBoard rows={centers} end={end} today={load.today} /> : null}

      {open ? (
        <CellPanel row={open.row} week={load.weeks[open.week]} weekIndex={open.week} line={filters.line} centerIds={centers.map((c) => c.workCenterId!)}
          canEdit={canEdit} recalculating={recalculating} onRecalculate={onRecalculate} onClose={() => setOpen(null)} />
      ) : null}
    </div>
  )
}

/** "Ocupação da fábrica por semana", "Ocupação de Corte por semana" ou "Ocupação: Total de Cabines, por semana". */
function chartTitle(row: FactoryLoadRow): string {
  if (row.workCenterId) return `Ocupação de ${row.name} por semana`
  return row.name === 'Fábrica' ? 'Ocupação da fábrica por semana' : `Ocupação: ${row.name}, por semana`
}

/** 400 px: uma semana por vez (seletor e setas de 44 px) com os centros em lista; tocar no centro abre as etapas. */
function MobileWeek({ weeks, index, rows, onWeek, onOpen }: {
  weeks: FactoryLoadWeek[]
  index: number
  rows: FactoryLoadRow[]
  onWeek: (index: number) => void
  onOpen: (row: FactoryLoadRow) => void
}) {
  return (
    <section aria-label="Ocupação da semana" className="space-y-2 md:hidden">
      <div className="flex items-center gap-2">
        <button type="button" className={iconButton} aria-label="Semana anterior" disabled={index === 0} onClick={() => onWeek(index - 1)}><ChevronLeft aria-hidden="true" className="size-4" /></button>
        <label className="min-w-0 flex-1">
          <span className="sr-only">Semana</span>
          <select className={cn(select, 'tabular-nums')} value={index} onChange={(e) => onWeek(Number(e.target.value))}>
            {weeks.map((w, i) => <option key={w.start} value={i}>{weekLabel(w)}</option>)}
          </select>
        </label>
        <button type="button" className={iconButton} aria-label="Próxima semana" disabled={index === weeks.length - 1} onClick={() => onWeek(index + 1)}><ChevronRight aria-hidden="true" className="size-4" /></button>
      </div>
      <ul className="divide-y divide-border rounded-md border border-border bg-card">
        {rows.map((row) => {
          const cell = row.cells[index]
          const chip = bandChip(cell.band)
          return (
            <li key={row.workCenterId ?? 'fabrica'}>
              <button type="button" onClick={() => onOpen(row)} data-testid="ocupacao-centro"
                className={cn('flex min-h-[44px] w-full items-center justify-between gap-3 px-3 py-2 text-left', row.workCenterId === null && 'font-semibold', cell.band === 'Over' && 'bg-crit-soft text-crit-ink')}>
                <span className="min-w-0">
                  <span className="block truncate">{row.name}</span>
                  <span className="block text-xs text-muted-foreground tabular-nums">{hoursOf(cell)}</span>
                </span>
                <span className="flex shrink-0 flex-col items-end gap-1">
                  <span className="font-semibold tabular-nums">{percentText(cell.percent)}</span>
                  {chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}
                </span>
              </button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

/** CA 3: a carga já planejada depois da última semana mostrada e o último dia planejado de cada centro. */
function AfterBoard({ rows, end, today }: { rows: FactoryLoadRow[]; end: string; today: string }) {
  const after = afterRows(rows)
  return (
    <section aria-labelledby="depois-de" className="rounded-md border border-border bg-card p-4">
      <h2 id="depois-de" className="flex items-center font-semibold tabular-nums">Depois de {shortDate(end, today)}<Dica nome="Depois de" texto={LOAD_DICAS.depoisDe} /></h2>
      {after.length === 0 ? (
        <p className="mt-1 text-sm text-muted-foreground">Toda a carga planejada cabe nas semanas mostradas.</p>
      ) : (
        <ul className="mt-2 space-y-1 text-sm" data-testid="depois-de">
          {after.map((r) => (
            <li key={r.workCenterId!} className="flex flex-wrap justify-between gap-x-3 tabular-nums">
              <span className="font-semibold">{r.name} ocupado até {shortDate(r.busyUntil!, today)}</span>
              <span className="text-muted-foreground">{hoursText(r.hoursAfter)} planejadas depois de {shortDate(end, today)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** CA 5: as etapas alocadas no centro e na semana, com link para a OS e o total igual ao da célula; propositivo pela faixa. */
function CellPanel({ row, week, weekIndex, line, centerIds, canEdit, recalculating, onRecalculate, onClose }: {
  row: FactoryLoadRow
  week: FactoryLoadWeek
  weekIndex: number
  line?: string
  centerIds: string[]
  canEdit: boolean
  recalculating: boolean
  onRecalculate: () => Promise<void>
  onClose: () => void
}) {
  const factory = row.workCenterId === null
  const cell = row.cells[weekIndex]
  const chip = bandChip(cell.band)
  const query = useGetFactoryLoadCell(factory ? { weekStart: week.start, workCenterIds: centerIds, line } : { workCenterId: row.workCenterId!, weekStart: week.start })
  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={`${row.name} · ${weekLabel(week)}`} description={`De ${formatDate(week.from)} a ${formatDate(week.end)}`}>
      <div className="space-y-4 text-sm">
        <p className="flex flex-wrap items-center gap-2 tabular-nums">
          <span className="font-semibold">{hoursOf(cell)}, {percentText(cell.percent)}</span>
          {chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}
        </p>
        {cell.band === 'Over' ? (
          <div className="space-y-2 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft p-3 text-crit-ink">
            <p>As horas planejadas passaram da capacidade: o calendário mudou depois do último cálculo. Recalcule para o plano redistribuir as etapas.</p>
            {canEdit ? <button type="button" className={primary} disabled={recalculating} onClick={() => void onRecalculate()}>{recalculating ? 'Recalculando…' : 'Recalcular agora'}</button>
              : <p>Peça a quem cuida da produção para recalcular o plano.</p>}
          </div>
        ) : cell.band === 'Idle' ? (
          <p className="rounded-md border border-border bg-muted p-3">
            Há folga nesta semana. <Link to={'/app/producao/planejamento' as never} search={{ aba: 'fila' } as never} className="font-semibold text-primary hover:underline">Ver a fila</Link> para antecipar um projeto.
          </p>
        ) : null}

        {query.isPending ? <Skeleton className="h-32" /> : query.isError ? (
          <ErrorState title="Não foi possível carregar as etapas." message="Tente de novo em instantes." onRetry={() => query.refetch()} />
        ) : query.data.steps.length === 0 ? (
          <p className="text-muted-foreground">Nenhuma etapa alocada nesta semana.</p>
        ) : (
          <>
            <p className="font-semibold tabular-nums" data-testid="celula-total">Total da semana: {hoursText(query.data.totalHours)}</p>
            <ul className="divide-y divide-border rounded-md border border-border" aria-label="Etapas alocadas">
              {query.data.steps.map((s) => (
                <li key={`${s.stepId}:${s.workCenterId}`} className="space-y-0.5 p-3">
                  <p className="flex flex-wrap justify-between gap-x-3">
                    <Link to={'/app/producao/os/$id' as never} params={{ id: s.orderId } as never} search={{ aba: 'etapas' } as never} className="font-semibold text-primary tabular-nums hover:underline">{s.orderNumber}</Link>
                    <span className="font-semibold tabular-nums">{hoursText(s.hours)}</span>
                  </p>
                  <p>{s.stepName}{factory ? ` · ${s.workCenterName}` : ''}</p>
                  {s.projectNumber ? <p className="text-muted-foreground"><span className="tabular-nums">{s.projectNumber}</span>{s.projectName ? ` · ${s.projectName}` : ''}</p> : null}
                  <p className="text-muted-foreground tabular-nums">{forecastText({ plannedStart: s.plannedStart, plannedEnd: s.plannedEnd })}</p>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </SidePanel>
  )
}
