import { ChevronDown, ChevronRight } from 'lucide-react'
import { type KeyboardEvent, type ReactNode, type Ref, useCallback, useEffect, useId, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { cn } from '@/lib/utils'
import { axis as makeAxis, barBox, type GanttScale, monthLabel, ticks as makeTicks, xOf } from './gantt-scale'
import { StatusChip, type StatusTone } from './status-chip'

/**
 * Barra do Gantt (ADR 0033): <c>planned</c> previsto (tracejado), <c>actual</c> realizado (contínuo), <c>progress</c> em andamento (a parte
 * feita preenchida e o resto tracejado), <c>readonly</c> datas digitadas (hachurado, só leitura) e <c>summary</c> a faixa do projeto. Nada
 * só por cor: traço, preenchimento e rótulo direto.
 */
export interface GanttBar {
  id: string
  start: string
  end: string
  kind: 'planned' | 'actual' | 'progress' | 'readonly' | 'summary'
  /** Proporção feita (0 a 1), na barra em andamento. */
  progress?: number
  /** Letra da etapa no estilo do StageIndicator. */
  letter?: string | null
  /** Rótulo direto depois da barra (ex.: "Define a entrega"). */
  label?: string
  /** Contorno de destaque (a etapa que define a entrega). */
  emphasis?: boolean
  /** Sub-linha (visão por centro). */
  lane?: number
  ariaLabel: string
  detail: { title: string; lines: string[] }
  /** Sem ação (a faixa do projeto); as outras abrem a OS. */
  inert?: boolean
}

/** Marca do dia no fundo da linha (visão por centro): ocupação nas faixas da PLN-04 e dia sem capacidade ou com exceção. */
export interface GanttDay {
  date: string
  tone: 'idle' | 'normal' | 'full' | 'over' | 'off'
  /** Texto do dia (sempre junto da cor): "02/11: Feriado Finados", "05/11: 8 de 8 h, 100%, Lotado". */
  title: string
  /** Nome curto escrito no dia (feriado ou parada). */
  mark?: string
}

export interface GanttRow {
  id: string
  label: string
  /** Segunda linha do rótulo (ex.: o cliente, o centro). */
  sublabel?: string
  depth: number
  /** undefined = não expande. */
  expanded?: boolean
  /** Faixa lateral: âmbar em risco, vermelha atrasado (CLAUDE.md). */
  stripe?: 'warn' | 'crit'
  /** Texto do estado da faixa, para não depender da cor ("Em risco", "Atrasado"). */
  stripeLabel?: string
  chip?: { tone: StatusTone; text: string }
  lanes?: number
  bars: GanttBar[]
  /** Referências em x da linha (ex.: "Prometido 05/11"). */
  markers?: { date: string; label: string }[]
  days?: GanttDay[]
}

export interface GanttLegendItem {
  sample: 'actual' | 'planned' | 'progress' | 'readonly' | 'summary' | 'marker' | 'today' | 'drive' | 'full' | 'over' | 'off'
  label: string
  extra?: ReactNode
}

export interface GanttHandle {
  scrollToToday: () => void
}

interface GanttProps {
  title: string
  summary: string
  rows: GanttRow[]
  from: string
  to: string
  today: string
  scale: GanttScale
  rowHeader: string
  legend: GanttLegendItem[]
  table: ReactNode
  onToggle?: (rowId: string) => void
  onActivate: (bar: GanttBar, row: GanttRow) => void
  handle?: Ref<GanttHandle>
  /** Altura máxima da área rolável. */
  maxHeight?: number
}

export const NAME_WIDTH = 280
export const LANE_HEIGHT = 36
const HEADER_HEIGHT = 44
const BUFFER_ROWS = 8
const BAR_HEIGHT = 18

const TONE: Record<GanttDay['tone'], string> = {
  idle: 'fill-info-soft',
  normal: 'fill-muted',
  full: 'fill-warn-soft',
  over: 'fill-crit-soft',
  off: 'fill-muted',
}

interface Placed {
  row: GanttRow
  index: number
  top: number
  height: number
}

/** Posição de cada linha (as linhas com sub-linhas ficam mais altas). */
function layout(rows: GanttRow[]): { placed: Placed[]; total: number } {
  let top = 0
  const placed = rows.map((row, index) => {
    const height = Math.max(row.lanes ?? 1, 1) * LANE_HEIGHT
    const item = { row, index, top, height }
    top += height
    return item
  })
  return { placed, total: top }
}

/** Primeira linha cujo fim passa de y (busca binária). */
function firstVisible(placed: Placed[], y: number): number {
  let lo = 0
  let hi = placed.length - 1
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    if (placed[mid].top + placed[mid].height <= y) lo = mid + 1
    else hi = mid
  }
  return lo
}

/** As barras de cada linha na ordem do tempo (a navegação por setas, CA 8). */
function ordered(row: GanttRow): GanttBar[] {
  return [...row.bars].sort((a, b) => a.start.localeCompare(b.start) || (a.lane ?? 0) - (b.lane ?? 0) || a.id.localeCompare(b.id))
}

/**
 * Gantt próprio em SVG (ADR 0033, PLN-03): coluna de nomes fixa, eixo de dia, semana ou mês, linha "Hoje", referências da linha
 * ("Prometido"), fundo do dia (ocupação e exceções), linhas virtualizadas (só as visíveis e uma margem são desenhadas), barras focáveis com
 * setas (linha e tempo), Enter abre, detalhe no ponteiro, no foco e no toque, resumo textual acima e tabela equivalente ("Ver como
 * tabela"). O desenho tem role="img" ligado à tabela por aria-describedby. Cores só por token. Só leitura: nada arrasta.
 */
export function Gantt({ title, summary, rows, from, to, today, scale, rowHeader, legend, table, onToggle, onActivate, handle, maxHeight = 560 }: GanttProps) {
  const tableId = useId()
  const titleId = useId()
  const tipId = useId()
  const patternId = useId().replace(/:/g, '')
  const scroller = useRef<HTMLDivElement>(null)
  const barRefs = useRef(new Map<string, SVGGElement>())
  const [view, setView] = useState({ top: 0, height: maxHeight })
  const [showTable, setShowTable] = useState(false)
  // A tabela só é montada na primeira vez que abre (1.500 linhas escondidas pesariam na abertura, CA 13); a descrição fica sempre ligada.
  const [tableMounted, setTableMounted] = useState(false)
  const [active, setActive] = useState<string | null>(null)
  const [tip, setTip] = useState<{ bar: GanttBar; row: GanttRow; touch: boolean } | null>(null)
  const pendingFocus = useRef<string | null>(null)
  const pointer = useRef<string>('mouse')

  const ax = useMemo(() => makeAxis(from, to, scale), [from, to, scale])
  const marks = useMemo(() => makeTicks(ax), [ax])
  const { placed, total } = useMemo(() => layout(rows), [rows])
  const todayX = today >= from && today <= to ? xOf(ax, today) : null
  // Só as barras que aparecem no período entram na navegação por setas.
  const lists = useMemo(() => rows.map((row) => ordered(row).filter((b) => barBox(ax, b.start, b.end))), [rows, ax])
  const barIndex = useMemo(() => {
    const map = new Map<string, { row: number; position: number }>()
    lists.forEach((list, r) => list.forEach((bar, position) => map.set(bar.id, { row: r, position })))
    return map
  }, [lists])
  const tabbable = active && barIndex.has(active) ? active : lists.flat()[0]?.id ?? null

  const start = placed.length === 0 ? 0 : Math.max(firstVisible(placed, view.top) - BUFFER_ROWS, 0)
  const endIndex = placed.length === 0 ? -1 : Math.min(firstVisible(placed, view.top + view.height) + BUFFER_ROWS, placed.length - 1)
  const visible = placed.slice(start, endIndex + 1)

  const onScroll = useCallback(() => {
    const el = scroller.current
    if (el) setView({ top: Math.max(el.scrollTop - HEADER_HEIGHT, 0), height: el.clientHeight || maxHeight })
  }, [maxHeight])
  useLayoutEffect(onScroll, [onScroll, total])

  const scrollToToday = useCallback(() => {
    const el = scroller.current
    if (!el || todayX === null) return
    el.scrollLeft = Math.max(todayX - Math.max((el.clientWidth - NAME_WIDTH) / 3, 0), 0)
  }, [todayX])
  useImperativeHandle(handle, () => ({ scrollToToday }), [scrollToToday])

  // CA 6: a referência "Hoje" fica visível ao abrir e ao trocar a escala.
  useEffect(() => {
    const el = scroller.current
    if (!el || todayX === null) return
    const visibleWidth = (el.clientWidth || 800) - NAME_WIDTH
    if (todayX < el.scrollLeft || todayX > el.scrollLeft + visibleWidth) scrollToToday()
  }, [scale, todayX, scrollToToday])

  // Foco pelo teclado numa linha fora da janela: rola até ela e foca depois de desenhar.
  useEffect(() => {
    const id = pendingFocus.current
    if (!id) return
    const el = barRefs.current.get(id)
    if (el) {
      pendingFocus.current = null
      el.focus({ preventScroll: true })
      el.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
    }
  })

  const focusBar = (id: string | undefined) => {
    if (!id) return
    setActive(id)
    const at = barIndex.get(id)
    const el = scroller.current
    if (at && el) {
      const p = placed[at.row]
      if (p.top < el.scrollTop - HEADER_HEIGHT || p.top + p.height > el.scrollTop - HEADER_HEIGHT + el.clientHeight - HEADER_HEIGHT) {
        el.scrollTop = Math.max(p.top - LANE_HEIGHT, 0)
        onScroll()
      }
    }

    pendingFocus.current = id
    const node = barRefs.current.get(id)
    if (node) {
      pendingFocus.current = null
      node.focus({ preventScroll: true })
      node.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
    }
  }

  /** Setas: esquerda e direita no tempo (na mesma linha); para cima e para baixo a barra mais perto no tempo na linha com barras ao lado. */
  const keys = (event: KeyboardEvent, bar: GanttBar, row: GanttRow) => {
    const at = barIndex.get(bar.id)
    if (!at) return
    const list = lists[at.row]
    if (event.key === 'ArrowRight') focusBar(list[at.position + 1]?.id)
    else if (event.key === 'ArrowLeft') focusBar(list[at.position - 1]?.id)
    else if (event.key === 'Home') focusBar(list[0]?.id)
    else if (event.key === 'End') focusBar(list.at(-1)?.id)
    else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      const step = event.key === 'ArrowDown' ? 1 : -1
      for (let r = at.row + step; r >= 0 && r < rows.length; r += step) {
        const candidates = lists[r]
        if (candidates.length === 0) continue
        const x0 = xOf(ax, bar.start)
        const target = candidates.reduce((best, b) => (Math.abs(xOf(ax, b.start) - x0) < Math.abs(xOf(ax, best.start) - x0) ? b : best), candidates[0])
        focusBar(target.id)
        break
      }
    } else if (event.key === 'Enter' || event.key === ' ') {
      if (!bar.inert) onActivate(bar, row)
    } else if (event.key === 'Escape') setTip(null)
    else return
    event.preventDefault()
  }

  const tipBox = tip ? (() => {
    const at = barIndex.get(tip.bar.id)
    const p = at ? placed[at.row] : null
    const box = barBox(ax, tip.bar.start, tip.bar.end)
    if (!p || !box) return null
    const top = p.top + ((tip.bar.lane ?? 0) + 1) * LANE_HEIGHT - 4
    return { top, left: Math.min(box.x, Math.max(ax.width - 260, 0)) }
  })() : null

  return (
    <figure className="space-y-3" aria-labelledby={titleId}>
      <div className="space-y-1">
        <h2 id={titleId} className="font-semibold">{title}</h2>
        <p className="text-sm tabular-nums" aria-live="polite" data-testid="gantt-resumo">{summary}</p>
      </div>

      <div ref={scroller} onScroll={onScroll} className="relative overflow-auto rounded-md border border-border bg-card" style={{ maxHeight }} data-testid="gantt-scroller">
        <div style={{ width: NAME_WIDTH + ax.width }}>
          <div className="sticky top-0 z-20 flex border-b border-border bg-card" style={{ height: HEADER_HEIGHT }}>
            <div className="sticky left-0 z-30 flex shrink-0 items-end border-r border-border bg-card px-3 pb-1.5 text-xs font-semibold text-muted-foreground" style={{ width: NAME_WIDTH }}>{rowHeader}</div>
            <svg aria-hidden="true" width={ax.width} height={HEADER_HEIGHT} className="shrink-0 text-[11px] tabular-nums">
              {scale === 'dia' ? marks.filter((t) => t.date.endsWith('-01') || t.date === from).map((t) => (
                <text key={`m${t.date}`} x={t.x + 2} y={14} className="fill-muted-foreground">{monthLabel(t.date)}</text>
              )) : null}
              {marks.map((t) => (
                <g key={t.date}>
                  <line x1={t.x} x2={t.x} y1={t.major ? 18 : 26} y2={HEADER_HEIGHT} className="stroke-border" />
                  <text x={t.x + 3} y={HEADER_HEIGHT - 8} className={cn(t.major ? 'fill-foreground font-semibold' : 'fill-muted-foreground')}>{t.label}</text>
                </g>
              ))}
              {todayX !== null ? (
                <g>
                  <rect x={todayX - 18} y={2} width={36} height={14} rx={3} className="fill-rail" />
                  <text x={todayX} y={13} textAnchor="middle" className="fill-signal font-semibold">Hoje</text>
                </g>
              ) : null}
            </svg>
          </div>

          <div className="relative flex" style={{ height: Math.max(total, LANE_HEIGHT) }}>
            <div className="sticky left-0 z-10 shrink-0 border-r border-border bg-card" style={{ width: NAME_WIDTH }}>
              {visible.map(({ row, top, height }) => (
                <div key={row.id} data-row={row.id} className={cn('absolute inset-x-0 flex items-center gap-1 border-b border-border pr-2 text-sm', row.stripe === 'crit' ? 'border-l-4 border-l-crit' : row.stripe === 'warn' ? 'border-l-4 border-l-warn' : 'border-l-4 border-l-transparent')}
                  style={{ top, height, paddingLeft: 4 + row.depth * 16 }}>
                  {row.expanded === undefined ? <span className="inline-block size-6 shrink-0" /> : (
                    <button type="button" aria-expanded={row.expanded} aria-label={`${row.expanded ? 'Recolher' : 'Abrir'} ${row.label}`} onClick={() => onToggle?.(row.id)}
                      className="grid size-6 shrink-0 place-items-center rounded-sm hover:bg-muted">
                      {row.expanded ? <ChevronDown aria-hidden="true" className="size-4" /> : <ChevronRight aria-hidden="true" className="size-4" />}
                    </button>
                  )}
                  <span className="min-w-0 flex-1 leading-tight">
                    <span className={cn('block truncate tabular-nums', row.depth === 0 && 'font-semibold')} title={row.label}>{row.label}</span>
                    {row.sublabel ? <span className="block truncate text-xs text-muted-foreground" title={row.sublabel}>{row.sublabel}</span> : null}
                  </span>
                  {row.stripeLabel ? <span className="sr-only">{row.stripeLabel}</span> : null}
                  {row.chip ? <StatusChip tone={row.chip.tone} className="shrink-0 tabular-nums">{row.chip.text}</StatusChip> : null}
                </div>
              ))}
            </div>

            <div className="relative shrink-0" style={{ width: ax.width, height: Math.max(total, LANE_HEIGHT) }}>
              <svg role="img" aria-label={title} aria-describedby={tableId} width={ax.width} height={Math.max(total, LANE_HEIGHT)} className="absolute inset-0">
                <defs>
                  <pattern id={patternId} width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
                    <line x1="0" y1="0" x2="0" y2="6" className="stroke-muted-foreground" strokeWidth="2" />
                  </pattern>
                </defs>
                {marks.map((t) => <line key={t.date} x1={t.x} x2={t.x} y1={0} y2={total} className={t.major ? 'stroke-border' : 'stroke-border/50'} />)}
                {visible.map(({ row, top, height }) => (
                  <g key={row.id}>
                    {(row.days ?? []).map((d) => {
                      const box = barBox(ax, d.date, d.date)
                      if (!box) return null
                      return (
                        <g key={d.date}>
                          <rect x={box.x} y={top} width={box.width} height={height} className={cn(TONE[d.tone], d.tone === 'off' ? 'opacity-70' : 'opacity-80')}>
                            <title>{d.title}</title>
                          </rect>
                          {d.mark && ax.dayWidth >= 12 ? (
                            <text x={box.x + box.width / 2} y={top + height - 4} textAnchor="middle" className="fill-muted-foreground text-[9px]">{ax.dayWidth >= 30 ? d.mark.slice(0, 6) : d.mark.charAt(0)}</text>
                          ) : null}
                        </g>
                      )
                    })}
                    <line x1={0} x2={ax.width} y1={top + height} y2={top + height} className="stroke-border" />
                    {(row.markers ?? []).map((m) => {
                      if (m.date < from || m.date > to) return null
                      const x = xOf(ax, m.date) + ax.dayWidth
                      return (
                        <g key={m.label} data-marker={m.label}>
                          <line x1={x} x2={x} y1={top + 2} y2={top + height - 2} className="stroke-foreground" strokeWidth={2} />
                          <text x={x + 3} y={top + 10} className="fill-foreground text-[10px] font-semibold tabular-nums">{m.label}</text>
                        </g>
                      )
                    })}
                  </g>
                ))}
                {todayX !== null ? <line x1={todayX} x2={todayX} y1={0} y2={total} className="stroke-signal" strokeWidth={2} strokeDasharray="2 3" /> : null}
              </svg>

              <svg role="group" aria-label={`Barras: ${title}`} width={ax.width} height={Math.max(total, LANE_HEIGHT)} className="absolute inset-0 overflow-visible">
                {visible.map(({ row, top }) => row.bars.map((bar) => {
                  const box = barBox(ax, bar.start, bar.end)
                  if (!box) return null
                  const y = top + (bar.lane ?? 0) * LANE_HEIGHT + (LANE_HEIGHT - BAR_HEIGHT) / 2
                  return (
                    <g key={bar.id} ref={(el) => { if (el) barRefs.current.set(bar.id, el); else barRefs.current.delete(bar.id) }}
                      role="button" tabIndex={bar.id === tabbable ? 0 : -1} aria-label={bar.ariaLabel} aria-describedby={tip?.bar.id === bar.id ? tipId : undefined}
                      aria-disabled={bar.inert || undefined} data-bar={bar.id} data-kind={bar.kind} data-drive={bar.emphasis ? '' : undefined}
                      className="cursor-pointer outline-none [&:focus-visible_.gantt-ring]:visible"
                      onFocus={() => { setActive(bar.id); setTip({ bar, row, touch: false }) }}
                      onBlur={() => setTip((t) => (t?.bar.id === bar.id && !t.touch ? null : t))}
                      onMouseEnter={() => setTip({ bar, row, touch: false })}
                      onMouseLeave={() => setTip((t) => (t?.bar.id === bar.id && !t.touch ? null : t))}
                      onPointerDown={(e) => { pointer.current = e.pointerType }}
                      onClick={() => {
                        if (pointer.current === 'touch' && !(tip?.touch && tip.bar.id === bar.id)) {
                          setTip({ bar, row, touch: true })
                          return
                        }

                        if (!bar.inert) onActivate(bar, row)
                      }}
                      onKeyDown={(e) => keys(e, bar, row)}>
                      <rect className="gantt-ring invisible fill-none stroke-ring" x={box.x - 3} y={y - 3} width={box.width + 6} height={BAR_HEIGHT + 6} rx={4} strokeWidth={2} />
                      <BarShape bar={bar} x={box.x} y={y} width={box.width} pattern={patternId} />
                      {bar.letter && box.width >= 20 ? (
                        <g aria-hidden="true">
                          <rect x={box.x + 1} y={y + 1} width={16} height={BAR_HEIGHT - 2} rx={3} className="fill-rail" />
                          <text x={box.x + 9} y={y + BAR_HEIGHT - 5} textAnchor="middle" className="fill-signal font-display text-[11px] font-extrabold">{bar.letter}</text>
                        </g>
                      ) : null}
                      {bar.label ? (
                        <text aria-hidden="true" x={box.x + box.width + 4} y={y + BAR_HEIGHT - 5} className={cn('text-[11px] font-semibold', bar.emphasis ? 'fill-warn-ink' : 'fill-muted-foreground')}>{bar.label}</text>
                      ) : null}
                    </g>
                  )
                }))}
              </svg>

              {tip && tipBox ? (
                <div id={tipId} role="tooltip" className="absolute z-40 w-64 rounded-md border border-border bg-popover p-3 text-xs text-popover-foreground shadow-md" style={{ top: tipBox.top, left: tipBox.left }}>
                  <p className="font-semibold">{tip.bar.detail.title}</p>
                  {tip.bar.detail.lines.map((line) => <p key={line} className="tabular-nums">{line}</p>)}
                  {tip.touch && !tip.bar.inert ? (
                    <button type="button" className="mt-2 inline-flex min-h-[44px] items-center rounded-md bg-primary px-3 font-semibold text-primary-foreground" onClick={() => onActivate(tip.bar, tip.row)}>Abrir OS</button>
                  ) : null}
                </div>
              ) : null}
            </div>
          </div>
        </div>
      </div>

      <figcaption className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
        {legend.map((item) => (
          <span key={item.label} className="inline-flex items-center gap-1.5">
            <LegendSample sample={item.sample} pattern={patternId} />
            {item.label}{item.extra}
          </span>
        ))}
        <span>Setas movem entre as barras; Enter abre a OS.</span>
      </figcaption>

      <div>
        <button type="button" aria-expanded={showTable} aria-controls={tableId} onClick={() => { setTableMounted(true); setShowTable((s) => !s) }} className="inline-flex min-h-[44px] items-center text-sm font-semibold text-primary">
          {showTable ? 'Ocultar a tabela' : 'Ver como tabela'}
        </button>
        <div id={tableId} hidden={!showTable} className="mt-2">{tableMounted ? table : <p>{summary} As mesmas linhas, com as datas em texto, estão em “Ver como tabela”.</p>}</div>
      </div>
    </figure>
  )
}

function BarShape({ bar, x, y, width, pattern }: { bar: GanttBar; x: number; y: number; width: number; pattern: string }) {
  const emphasis = bar.emphasis ? 'stroke-warn' : 'stroke-primary'
  const strokeWidth = bar.emphasis ? 3 : 1.5
  switch (bar.kind) {
    case 'summary':
      return <rect x={x} y={y + 5} width={width} height={BAR_HEIGHT - 10} rx={2} className="fill-muted-foreground" />
    case 'actual':
      return <rect x={x} y={y} width={width} height={BAR_HEIGHT} rx={3} className={cn('fill-primary', emphasis)} strokeWidth={strokeWidth} />
    case 'readonly':
      return <rect x={x} y={y} width={width} height={BAR_HEIGHT} rx={3} fill={`url(#${pattern})`} className="stroke-muted-foreground" strokeWidth={1.5} />
    case 'progress': {
      const done = Math.max(Math.min(bar.progress ?? 0, 1), 0) * width
      return (
        <>
          <rect x={x} y={y} width={width} height={BAR_HEIGHT} rx={3} className={cn('fill-primary-soft', emphasis)} strokeWidth={strokeWidth} strokeDasharray="5 3" />
          {done > 0 ? <rect data-done="" x={x} y={y} width={done} height={BAR_HEIGHT} rx={3} className="fill-primary" /> : null}
        </>
      )
    }
    default:
      return <rect x={x} y={y} width={width} height={BAR_HEIGHT} rx={3} className={cn('fill-primary-soft', emphasis)} strokeWidth={strokeWidth} strokeDasharray="5 3" />
  }
}

function LegendSample({ sample, pattern }: { sample: GanttLegendItem['sample']; pattern: string }) {
  const box = (className: string, extra: Record<string, string | number> = {}) => <rect x="1" y="2" width="22" height="10" rx="2" className={className} {...extra} />
  return (
    <svg aria-hidden="true" width="24" height="14">
      {sample === 'actual' ? box('fill-primary stroke-primary') : null}
      {sample === 'planned' ? box('fill-primary-soft stroke-primary', { strokeDasharray: '4 2' }) : null}
      {sample === 'progress' ? <>{box('fill-primary-soft stroke-primary', { strokeDasharray: '4 2' })}<rect x="1" y="2" width="11" height="10" rx="2" className="fill-primary" /></> : null}
      {sample === 'readonly' ? box('stroke-muted-foreground', { fill: `url(#${pattern})` }) : null}
      {sample === 'summary' ? <rect x="1" y="5" width="22" height="4" rx="1" className="fill-muted-foreground" /> : null}
      {sample === 'drive' ? box('fill-primary-soft stroke-warn', { strokeWidth: 2.5 }) : null}
      {sample === 'marker' ? <line x1="12" x2="12" y1="0" y2="14" className="stroke-foreground" strokeWidth="2" /> : null}
      {sample === 'today' ? <line x1="12" x2="12" y1="0" y2="14" className="stroke-signal" strokeWidth="2" strokeDasharray="2 2" /> : null}
      {sample === 'full' ? box('fill-warn-soft') : null}
      {sample === 'over' ? box('fill-crit-soft') : null}
      {sample === 'off' ? box('fill-muted') : null}
    </svg>
  )
}
