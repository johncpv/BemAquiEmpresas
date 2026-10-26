import type { QueryClient } from '@tanstack/react-query'
import { keepPreviousData } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useCallback, useMemo, useRef, useState } from 'react'
import { getGetCentersGanttQueryKey, getGetProjectsGanttQueryKey, useGetCentersGantt, useGetProjectGantt, useGetProjectsGantt } from '@/api/generated/bemaqui'
import type { GanttCenterRow, GanttCentersView, GanttProject, GanttProjectsView } from '@/api/generated/model'
import { Gantt, type GanttBar, type GanttHandle, type GanttLegendItem } from '@/components/ui/gantt'
import { type GanttScale, SCALE_LABEL } from '@/components/ui/gantt-scale'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { type TreeColumn, type TreeNode, TreeTable } from '@/components/ui/tree-table'
import { Dica } from '@/help/Dica'
import { useMediaQuery } from '@/hooks/useMediaQuery'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useProductionLive } from '../kanban/useBoardLive'
import {
  businessToday, centerRows, centersSummary, defaultOpen, GANTT_DICAS, type GanttFilters, ganttFiltersToSearch, orderOfBar, parseGanttFilters, PERIOD_LABEL, type GanttPeriodo,
  periodRange, projectEnd, projectName, projectRows, reasonText, short, simpleBar, slackChip, stepSublabel, summaryLine,
} from './gantt'
import { planLine } from './load'

const action = 'inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'
const select = 'min-h-[44px] w-full rounded-md border border-input bg-background px-2 text-sm sm:w-auto'
/** A partir daqui o Gantt; abaixo, a lista (400 px). Só um dos dois é montado (CA 13). */
const WIDE = '(min-width: 768px)'
const iconButton = 'inline-flex min-h-[44px] min-w-[44px] shrink-0 items-center justify-center rounded-md border border-border bg-card disabled:opacity-40'

/** CA 12: o sinal do hub recarrega o cronograma aberto, agrupado a cada 5 s (sem piscar: a tela fica com os dados até chegar a resposta). */
export const GANTT_RELOAD_MS = 5000

const reloadGantt = (client: QueryClient) => {
  void client.invalidateQueries({ queryKey: getGetProjectsGanttQueryKey() })
  void client.invalidateQueries({ queryKey: getGetCentersGanttQueryKey() })
  void client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].endsWith('/gantt') })
}

function useGanttLive() {
  return useProductionLive(reloadGantt, { schedule: true, delay: GANTT_RELOAD_MS })
}

const PROJECT_LEGEND: GanttLegendItem[] = [
  { sample: 'actual', label: 'Realizado' },
  { sample: 'planned', label: 'Previsto', extra: <Dica nome="Tracejado" texto={GANTT_DICAS.tracejado} /> },
  { sample: 'progress', label: 'Em andamento (parte feita preenchida)' },
  { sample: 'readonly', label: 'Instalação (datas da OS)', extra: <Dica nome="Instalação (datas da OS)" texto={GANTT_DICAS.instalacao} /> },
  { sample: 'marker', label: 'Prometido', extra: <Dica nome="Prometido" texto={GANTT_DICAS.prometido} /> },
  { sample: 'drive', label: 'Define a entrega', extra: <Dica nome="Define a entrega" texto={GANTT_DICAS.defineEntrega} /> },
  { sample: 'today', label: 'Hoje' },
]

const CENTER_LEGEND: GanttLegendItem[] = [
  { sample: 'planned', label: 'Previsto', extra: <Dica nome="Tracejado" texto={GANTT_DICAS.tracejado} /> },
  { sample: 'progress', label: 'Em andamento (parte feita preenchida)' },
  { sample: 'full', label: 'Dia lotado (95% ou mais)' },
  { sample: 'over', label: 'Acima da capacidade' },
  { sample: 'off', label: 'Sem expediente, feriado ou parada' },
  { sample: 'today', label: 'Hoje' },
]

/**
 * Planejamento → Cronograma (PLN-03): por projeto (projeto → OS → etapa, com prevista × prometida, CA 1 a 4) ou por centro (CA 5), escala
 * dia, semana ou mês e "Ir para hoje" (CA 6), resumo textual (CA 7), detalhe e teclado (CA 8), filtros na URL (CA 9), tabela equivalente
 * (CA 10) e recarga pelo hub (CA 12). A 400 px, lista por projeto e um centro por vez.
 */
export function GanttTab() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters = parseGanttFilters(search)
  const setFilters = (next: Partial<GanttFilters>) =>
    void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...ganttFiltersToSearch({ ...filters, ...next }) }) } as never)
  useGanttLive()
  const range = periodRange(filters.period, businessToday())

  return (
    <div className="space-y-4">
      <div role="group" aria-label="Visão do cronograma" className="flex flex-wrap gap-1">
        {(['projeto', 'centro'] as const).map((v) => (
          <button key={v} type="button" aria-pressed={filters.view === v} onClick={() => setFilters({ view: v })}
            className={cn('inline-flex min-h-[44px] items-center rounded-md border border-border px-4 text-sm font-semibold', filters.view === v ? 'bg-primary text-primary-foreground' : 'bg-card')}>
            {v === 'projeto' ? 'Por projeto' : 'Por centro'}
          </button>
        ))}
      </div>
      {filters.view === 'centro'
        ? <CentersGantt filters={filters} range={range} setFilters={setFilters} />
        : <ProjectsGantt filters={filters} range={range} setFilters={setFilters} />}
    </div>
  )
}

/** Projeto 360 → Cronograma (CA 11): a visão por projeto só desse projeto, com o período que cabe o projeto inteiro. */
export function ProjectGanttTab({ projectId }: { projectId: string }) {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters = parseGanttFilters(search)
  useGanttLive()
  const query = useGetProjectGantt(projectId, undefined, { query: { placeholderData: keepPreviousData } })
  if (query.isPending) return <GanttSkeleton />
  if (query.isError) return <ErrorState title="Não foi possível carregar o cronograma." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
  return (
    <ProjectsContent view={query.data} filters={filters} title="Cronograma do projeto" single
      onScale={(scale) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, escala: scale === 'semana' ? undefined : scale }) } as never)} />
  )
}

function GanttSkeleton() {
  return (
    <div aria-busy="true" aria-label="Carregando o cronograma" className="space-y-2">
      {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-9" />)}
    </div>
  )
}

function ProjectsGantt({ filters, range, setFilters }: { filters: GanttFilters; range: { from?: string; to?: string }; setFilters: (f: Partial<GanttFilters>) => void }) {
  const query = useGetProjectsGantt({ ...range, projectId: filters.project, partyId: filters.party, line: filters.line, workCenterId: filters.center, atRisk: filters.atRisk || undefined },
    { query: { placeholderData: keepPreviousData } })
  if (query.isPending) return <GanttSkeleton />
  if (query.isError) return <ErrorState title="Não foi possível carregar o cronograma." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
  const view = query.data
  const filtered = !!(filters.project || filters.party || filters.line || filters.center || filters.atRisk)
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
          <span className="font-semibold">Projeto</span>
          <select className={select} value={filters.project ?? ''} onChange={(e) => setFilters({ project: e.target.value || undefined })}>
            <option value="">Todos os projetos</option>
            {view.projectOptions.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
          </select>
        </label>
        <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
          <span className="font-semibold">Cliente</span>
          <select className={select} value={filters.party ?? ''} onChange={(e) => setFilters({ party: e.target.value || undefined })}>
            <option value="">Todos os clientes</option>
            {view.partyOptions.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
          </select>
        </label>
        <LineCenterFilters lines={view.lines} centers={view.centers} filters={filters} setFilters={setFilters} />
        <label className="inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold">
          <input type="checkbox" className="size-5" checked={filters.atRisk} onChange={(e) => setFilters({ atRisk: e.target.checked })} />
          Só em risco
        </label>
      </div>
      {view.projects.length === 0 ? (
        filtered ? (
          <EmptyState title="Nenhum projeto com esse filtro." description="Escolha outro projeto, cliente, linha ou centro, ou tire o “Só em risco”."
            action={<button type="button" className={action} onClick={() => setFilters({ project: undefined, party: undefined, line: undefined, center: undefined, atRisk: false })}>Limpar filtros</button>} />
        ) : (
          <EmptyState title="Nenhuma etapa planejada no período." description="Quando o plano datar as etapas das OS de fabricação, os projetos aparecem aqui na ordem da fila."
            action={<Link to={'/app/producao/planejamento' as never} search={{ aba: 'fila' } as never} className={action}>Ver a fila</Link>} />
        )
      ) : (
        <ProjectsContent view={view} filters={filters} title="Cronograma por projeto" onScale={(scale) => setFilters({ scale })} onPeriod={(period) => setFilters({ period })} />
      )}
    </div>
  )
}

function LineCenterFilters({ lines, centers, filters, setFilters }: { lines: string[]; centers: { id: string; label: string }[]; filters: GanttFilters; setFilters: (f: Partial<GanttFilters>) => void }) {
  return (
    <>
      <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
        <span className="font-semibold">Linha</span>
        <select className={select} value={filters.line ?? ''} onChange={(e) => setFilters({ line: e.target.value || undefined, center: undefined })}>
          <option value="">Todas as linhas</option>
          {lines.map((l) => <option key={l} value={l}>{l}</option>)}
        </select>
      </label>
      <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
        <span className="font-semibold">Centro</span>
        <select className={select} value={filters.center ?? ''} onChange={(e) => setFilters({ center: e.target.value || undefined })}>
          <option value="">Todos os centros</option>
          {centers.map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
        </select>
      </label>
    </>
  )
}

/** Escala, período e "Ir para hoje" (CA 6). */
function ScaleBar({ scale, period, onScale, onPeriod, onToday, plan, today }: {
  scale: GanttScale; period?: GanttPeriodo; onScale: (s: GanttScale) => void; onPeriod?: (p: GanttPeriodo) => void; onToday: () => void; plan: string | null | undefined; today: string
}) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div role="group" aria-label="Escala" className="flex gap-1">
        {(['dia', 'semana', 'mes'] as const).map((s) => (
          <button key={s} type="button" aria-pressed={scale === s} onClick={() => onScale(s)}
            className={cn('inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold', scale === s ? 'bg-primary text-primary-foreground' : 'bg-card')}>
            {SCALE_LABEL[s]}
          </button>
        ))}
      </div>
      {onPeriod ? (
        <label className="flex items-center gap-2 text-sm">
          <span className="font-semibold">Período</span>
          <select className={cn(select, 'sm:w-auto')} value={period} onChange={(e) => onPeriod(e.target.value as GanttPeriodo)}>
            {(Object.keys(PERIOD_LABEL) as GanttPeriodo[]).map((p) => <option key={p} value={p}>{PERIOD_LABEL[p]}</option>)}
          </select>
        </label>
      ) : null}
      <button type="button" className={cn(action, 'hidden md:inline-flex')} onClick={onToday}>Ir para hoje</button>
      <span className="text-sm font-semibold tabular-nums text-muted-foreground">{planLine(plan, today)}</span>
    </div>
  )
}

interface ProjectNode extends TreeNode {
  center?: string
  start?: string | null
  end?: string | null
  promised?: string | null
  slack?: string
  state?: string
  orderId?: string
  installation?: boolean
  children?: ProjectNode[]
}

/** A tabela equivalente (CA 10): projeto → OS → etapa com as datas em texto. */
function projectNodes(view: GanttProjectsView): ProjectNode[] {
  return view.projects.map((p) => ({
    id: `t:p:${p.projectId}`, label: projectName(p), start: p.start, end: p.forecastDate, promised: p.promisedDate, slack: slackChip(p)?.text ?? '—',
    state: p.risk === 'Late' ? 'Atrasado' : p.risk === 'AtRisk' ? 'Em risco' : p.forecastDate ? 'No prazo' : `Sem previsão: ${reasonText(p.noForecastReason)}`,
    children: p.orders.map((o) => ({
      id: `t:o:${o.orderId}`, label: o.kind === 'Installation' ? `${o.number} · Instalação (datas da OS)` : o.number, start: o.start, end: o.end, orderId: o.orderId,
      installation: o.kind === 'Installation', state: o.kind === 'Installation' ? 'Só leitura' : undefined,
      children: o.steps.map((s) => ({
        id: `t:s:${s.stepId}`, label: `${s.code ? `${s.code} · ` : ''}${s.name}`, center: s.workCenterName ?? 'Sem centro',
        start: s.state === 'Done' ? s.actualStart : s.plannedStart, end: s.state === 'Done' ? s.actualEnd : s.plannedEnd,
        state: s.state === 'Done' ? 'Concluída (real)' : s.state === 'InProgress' ? `Em andamento, ${Math.round(s.progress * 100)}% feito` : s.state === 'Planned' ? 'Prevista' : stepSublabel(s),
        slack: s.drivesDelivery ? 'Define a entrega' : undefined,
      })),
    })),
  }))
}

const PROJECT_COLUMNS: TreeColumn<ProjectNode>[] = [
  { header: 'Centro', cell: (n) => n.center ?? '' },
  { header: 'Início', cell: (n) => (n.start ? formatDate(n.start) : '—'), numeric: true },
  { header: 'Fim', cell: (n) => (n.end ? formatDate(n.end) : '—'), numeric: true },
  { header: 'Prometido', cell: (n) => (n.promised ? formatDate(n.promised) : ''), numeric: true },
  { header: 'Folga', cell: (n) => n.slack ?? '', numeric: true },
  { header: 'Situação', cell: (n) => n.state ?? '' },
]

function ProjectsContent({ view, filters, title, single = false, onScale, onPeriod }: {
  view: GanttProjectsView; filters: GanttFilters; title: string; single?: boolean; onScale: (s: GanttScale) => void; onPeriod?: (p: GanttPeriodo) => void
}) {
  const navigate = useNavigate()
  const wide = useMediaQuery(WIDE)
  const ref = useRef<GanttHandle>(null)
  const ids = view.projects.map((p) => p.projectId).join(',')
  const [open, setOpen] = useState<{ key: string; set: Set<string> }>(() => ({ key: ids, set: defaultOpen(view) }))
  const current = open.key === ids ? open.set : defaultOpen(view)
  const rows = useMemo(() => projectRows(view, current), [view, current])
  const nodes = useMemo(() => projectNodes(view), [view])
  const toggle = useCallback((id: string) => setOpen((o) => {
    const set = new Set(o.key === ids ? o.set : defaultOpen(view))
    if (set.has(id)) set.delete(id)
    else set.add(id)
    return { key: ids, set }
  }), [ids, view])
  const noForecast = view.projects.reduce((n, p) => n + p.orders.reduce((m, o) => m + o.steps.filter((s) => s.state === 'NoForecast').length, 0), 0)
  const activate = (bar: GanttBar) => {
    const target = orderOfBar(bar.id, view, null)
    if (!target) return
    void navigate({ to: (target.installation ? '/app/producao/instalacoes/$id' : '/app/producao/os/$id') as never, params: { id: target.orderId } as never })
  }

  return (
    <div className="space-y-3">
      <ScaleBar scale={filters.scale} period={single ? undefined : filters.period} onScale={onScale} onPeriod={onPeriod} onToday={() => ref.current?.scrollToToday()} plan={view.planComputedAt} today={view.today} />
      {wide ? (
        <Gantt handle={ref} title={title} summary={summaryLine(view.projects)} rows={rows} from={view.from} to={view.to} today={view.today} scale={filters.scale}
          rowHeader="Projeto / OS / Etapa" legend={PROJECT_LEGEND} onToggle={toggle} onActivate={activate}
          table={<TreeTable nodes={nodes} label="Cronograma em tabela" labelHeader="Projeto / OS / Etapa" columns={PROJECT_COLUMNS}
            renderLabel={(n) => (n.orderId ? <Link to={(n.installation ? '/app/producao/instalacoes/$id' : '/app/producao/os/$id') as never} params={{ id: n.orderId } as never} className="font-semibold text-primary tabular-nums hover:underline">{n.label}</Link> : <span className="tabular-nums">{n.label}</span>)} />} />
      ) : null}
      {noForecast > 0 ? (
        <p className="rounded-md border border-l-4 border-border border-l-warn bg-card px-3 py-2 text-sm">
          {noForecast === 1 ? '1 etapa sem previsão' : `${noForecast} etapas sem previsão`}: o plano não conseguiu datar.{' '}
          <Link to={'/app/producao/planejamento' as never} search={{ aba: 'sem-previsao' } as never} className="font-semibold text-primary underline">Ver o motivo</Link>
        </p>
      ) : null}
      {wide ? null : <ProjectList view={view} />}
    </div>
  )
}

/** 400 px (Telas e estados): lista por projeto (previsto, prometido, folga); ao abrir, as etapas com as datas em texto e uma barra simples. */
function ProjectList({ view }: { view: GanttProjectsView }) {
  const [open, setOpen] = useState<string | null>(view.projects.length === 1 ? view.projects[0].projectId : null)
  return (
    <section aria-label="Cronograma em lista" className="space-y-2 md:hidden">
      <p className="text-sm tabular-nums" data-testid="gantt-resumo-lista">{summaryLine(view.projects)}</p>
      <ul className="divide-y divide-border rounded-md border border-border bg-card">
        {view.projects.map((p) => {
          const chip = slackChip(p)
          const expanded = open === p.projectId
          return (
            <li key={p.projectId} className={cn('border-l-4', p.risk === 'Late' ? 'border-l-crit' : p.risk === 'AtRisk' ? 'border-l-warn' : 'border-l-transparent')}>
              <button type="button" aria-expanded={expanded} onClick={() => setOpen(expanded ? null : p.projectId)} data-testid="gantt-projeto"
                className="flex min-h-[44px] w-full items-center justify-between gap-3 px-3 py-2 text-left">
                <span className="min-w-0">
                  <span className="block truncate font-semibold tabular-nums">{projectName(p)}</span>
                  <span className="block text-xs text-muted-foreground tabular-nums">
                    Previsto {p.forecastDate ? short(p.forecastDate, view.today) : 'sem data'} · Prometido {p.promisedDate ? short(p.promisedDate, view.today) : '—'}
                  </span>
                </span>
                {chip ? <StatusChip tone={chip.tone} className="shrink-0 tabular-nums">{chip.text}</StatusChip> : null}
              </button>
              {expanded ? <ProjectSteps project={p} from={view.from} to={view.to} /> : null}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

function ProjectSteps({ project, from, to }: { project: GanttProject; from: string; to: string }) {
  return (
    <ul className="space-y-2 px-3 pb-3 text-sm" aria-label={`Etapas de ${projectName(project)}`}>
      {project.orders.map((o) => (
        <li key={o.orderId} className="space-y-1">
          <Link to={(o.kind === 'Installation' ? '/app/producao/instalacoes/$id' : '/app/producao/os/$id') as never} params={{ id: o.orderId } as never}
            className="inline-flex min-h-[44px] items-center font-semibold text-primary tabular-nums">{o.number}{o.kind === 'Installation' ? ' · Instalação (datas da OS)' : ''}</Link>
          {o.kind === 'Installation' && o.start && o.end ? <SimpleLine text={`${formatDate(o.start)} → ${formatDate(o.end)}`} start={o.start} end={o.end} from={from} to={to} kind="readonly" /> : null}
          {o.steps.map((s) => {
            const start = s.state === 'Done' ? s.actualStart : s.plannedStart
            const end = s.state === 'Done' ? s.actualEnd : s.plannedEnd
            return (
              <div key={s.stepId} className="space-y-0.5">
                <p className="flex flex-wrap justify-between gap-x-2">
                  <span>{s.code ? `${s.code} · ` : ''}{s.name}{s.drivesDelivery ? <strong className="text-warn-ink"> · Define a entrega</strong> : null}</span>
                  <span className="text-muted-foreground">{s.workCenterName ?? 'Sem centro'}</span>
                </p>
                {start && end ? (
                  <SimpleLine text={`${s.state === 'Done' ? 'Real' : 'Previsto'} ${formatDate(start)} → ${formatDate(end)}`} start={start} end={end} from={from} to={to}
                    kind={s.state === 'Done' ? 'actual' : 'planned'} />
                ) : (
                  <p className="text-warn-ink">{stepSublabel(s)}{' '}<Link to={'/app/producao/planejamento' as never} search={{ aba: 'sem-previsao' } as never} className="font-semibold text-primary underline">Ver o motivo</Link></p>
                )}
              </div>
            )
          })}
        </li>
      ))}
      {projectEnd(project) === null ? <li className="text-muted-foreground">Sem datas no plano ainda.</li> : null}
    </ul>
  )
}

function SimpleLine({ text, start, end, from, to, kind }: { text: string; start: string; end: string; from: string; to: string; kind: 'actual' | 'planned' | 'readonly' }) {
  const box = simpleBar(start, end, from, to)
  return (
    <div>
      <p className="text-xs tabular-nums text-muted-foreground">{text}</p>
      <div aria-hidden="true" className="relative mt-0.5 h-2 rounded-sm bg-muted">
        {box ? <span className={cn('absolute inset-y-0 rounded-sm', kind === 'actual' ? 'bg-primary' : kind === 'readonly' ? 'border border-muted-foreground bg-muted' : 'border border-dashed border-primary bg-primary-soft')}
          style={{ left: `${box.left}%`, width: `${box.width}%` }} /> : null}
      </div>
    </div>
  )
}

function CentersGantt({ filters, range, setFilters }: { filters: GanttFilters; range: { from?: string; to?: string }; setFilters: (f: Partial<GanttFilters>) => void }) {
  const query = useGetCentersGantt({ ...range, line: filters.line, workCenterId: filters.center }, { query: { placeholderData: keepPreviousData } })
  if (query.isPending) return <GanttSkeleton />
  if (query.isError) return <ErrorState title="Não foi possível carregar o cronograma." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
  const view = query.data
  if (view.activeCenters === 0) {
    return (
      <EmptyState title="Cadastre os centros de trabalho para ver o cronograma por centro." description="Cada centro mostra as etapas alocadas nele, dia a dia."
        action={<Link to={'/app/producao/centros-de-trabalho' as never} className={action}>Centros de trabalho</Link>} />
    )
  }

  return <CentersContent view={view} filters={filters} setFilters={setFilters} />
}

interface CenterNode extends TreeNode {
  start?: string
  end?: string
  orderId?: string
  info?: string
  children?: CenterNode[]
}

const CENTER_COLUMNS: TreeColumn<CenterNode>[] = [
  { header: 'Início', cell: (n) => (n.start ? formatDate(n.start) : ''), numeric: true },
  { header: 'Fim', cell: (n) => (n.end ? formatDate(n.end) : ''), numeric: true },
  { header: 'Detalhe', cell: (n) => n.info ?? '' },
]

function centerNodes(centers: GanttCenterRow[]): CenterNode[] {
  return centers.map((c) => ({
    id: `t:w:${c.workCenterId}`, label: c.name, info: c.line ?? undefined,
    children: [
      ...c.bars.map((b) => ({
        id: `t:c:${b.stepId}`, label: `${b.orderNumber} · ${b.code ? `${b.code} · ` : ''}${b.stepName}`, start: b.plannedStart, end: b.plannedEnd, orderId: b.orderId,
        info: `${b.projectNumber ?? 'Projeto removido'} · ${b.state === 'InProgress' ? `em andamento, ${Math.round(b.progress * 100)}% feito` : 'prevista'}`,
      })),
      ...c.days.filter((d) => d.exceptionName).map((d) => ({ id: `t:d:${c.workCenterId}:${d.date}`, label: `${d.exceptionType === 'Stoppage' ? 'Parada' : 'Feriado'}: ${d.exceptionName}`, start: d.date, end: d.date })),
    ],
  }))
}

function CentersContent({ view, filters, setFilters }: { view: GanttCentersView; filters: GanttFilters; setFilters: (f: Partial<GanttFilters>) => void }) {
  const navigate = useNavigate()
  const wide = useMediaQuery(WIDE)
  const ref = useRef<GanttHandle>(null)
  const rows = useMemo(() => centerRows(view.centers), [view.centers])
  const nodes = useMemo(() => centerNodes(view.centers), [view.centers])
  const [index, setIndex] = useState(0)
  const empty = view.centers.every((c) => c.bars.length === 0)
  const activate = (bar: GanttBar) => {
    const target = orderOfBar(bar.id, null, view.centers)
    if (target) void navigate({ to: '/app/producao/os/$id' as never, params: { id: target.orderId } as never })
  }

  const allCenters = view.centers.map((c) => ({ id: c.workCenterId, label: c.name }))
  const at = Math.min(index, Math.max(view.centers.length - 1, 0))
  const center = view.centers[at]
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <LineCenterFilters lines={view.lines} centers={allCenters} filters={filters} setFilters={setFilters} />
      </div>
      <ScaleBar scale={filters.scale} period={filters.period} onScale={(scale) => setFilters({ scale })} onPeriod={(period) => setFilters({ period })} onToday={() => ref.current?.scrollToToday()}
        plan={view.planComputedAt} today={view.today} />
      {view.centers.length === 0 ? (
        <EmptyState title="Nenhum centro com esse filtro." description="Escolha outra linha ou outro centro."
          action={<button type="button" className={action} onClick={() => setFilters({ line: undefined, center: undefined })}>Limpar filtros</button>} />
      ) : empty ? (
        <EmptyState title="Nenhuma etapa planejada no período." description="Quando o plano datar as etapas, elas aparecem aqui no centro de cada uma."
          action={<Link to={'/app/producao/planejamento' as never} search={{ aba: 'fila' } as never} className={action}>Ver a fila</Link>} />
      ) : (
        <>
          {wide ? (
            <Gantt handle={ref} title="Cronograma por centro de trabalho" summary={centersSummary(view.centers)} rows={rows} from={view.from} to={view.to} today={view.today}
              scale={filters.scale} rowHeader="Centro de trabalho" legend={CENTER_LEGEND} onActivate={activate}
              table={<TreeTable nodes={nodes} label="Cronograma por centro em tabela" labelHeader="Centro / Etapa" columns={CENTER_COLUMNS}
                renderLabel={(n) => (n.orderId ? <Link to={'/app/producao/os/$id' as never} params={{ id: n.orderId } as never} className="font-semibold text-primary tabular-nums hover:underline">{n.label}</Link> : <span>{n.label}</span>)} />} />
          ) : null}
          {!wide && center ? (
            <section aria-label="Cronograma do centro" className="space-y-2 md:hidden">
              <p className="text-sm tabular-nums">{centersSummary(view.centers)}</p>
              <div className="flex items-center gap-2">
                <button type="button" className={iconButton} aria-label="Centro anterior" disabled={at === 0} onClick={() => setIndex(at - 1)}><ChevronLeft aria-hidden="true" className="size-4" /></button>
                <label className="min-w-0 flex-1">
                  <span className="sr-only">Centro</span>
                  <select className={select} value={at} onChange={(e) => setIndex(Number(e.target.value))}>
                    {view.centers.map((c, i) => <option key={c.workCenterId} value={i}>{c.name}</option>)}
                  </select>
                </label>
                <button type="button" className={iconButton} aria-label="Próximo centro" disabled={at >= view.centers.length - 1} onClick={() => setIndex(at + 1)}><ChevronRight aria-hidden="true" className="size-4" /></button>
              </div>
              <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" data-testid="gantt-centro-lista">
                {center.bars.length === 0 ? <li className="p-3 text-muted-foreground">Nenhuma etapa neste centro no período.</li> : center.bars.map((b) => (
                  <li key={b.stepId} className="space-y-1 p-3">
                    <Link to={'/app/producao/os/$id' as never} params={{ id: b.orderId } as never} className="inline-flex min-h-[44px] items-center font-semibold text-primary tabular-nums">
                      {b.orderNumber} · {b.code ? `${b.code} · ` : ''}{b.stepName}
                    </Link>
                    <SimpleLine text={`Previsto ${formatDate(b.plannedStart)} → ${formatDate(b.plannedEnd)}`} start={b.plannedStart} end={b.plannedEnd} from={view.from} to={view.to} kind="planned" />
                  </li>
                ))}
                {center.days.filter((d) => d.exceptionName).map((d) => (
                  <li key={d.date} className="p-3 text-muted-foreground tabular-nums">{formatDate(d.date)}: {d.exceptionType === 'Stoppage' ? 'Parada' : 'Feriado'} {d.exceptionName}</li>
                ))}
              </ul>
            </section>
          ) : null}
        </>
      )}
    </div>
  )
}
