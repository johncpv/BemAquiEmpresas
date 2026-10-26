/**
 * Escala do Gantt (ADR 0033, PLN-03 CA 6), pura: datas de negócio ("AAAA-MM-DD", sem hora e sem fuso, para não mudar o dia) viram
 * número de dia e pixels. Dia, semana ou mês só mudam a largura do dia e as marcas do eixo.
 */
export type GanttScale = 'dia' | 'semana' | 'mes'

export const SCALE_LABEL: Record<GanttScale, string> = { dia: 'Dia', semana: 'Semana', mes: 'Mês' }

/** Pixels por dia em cada escala. */
export const DAY_WIDTH: Record<GanttScale, number> = { dia: 32, semana: 12, mes: 4 }

const MS_DAY = 86_400_000

/** Número do dia (dias desde 1970-01-01) de uma data "AAAA-MM-DD". */
export function dayNumber(date: string): number {
  const [y, m, d] = date.split('-').map(Number)
  return Math.round(Date.UTC(y, m - 1, d) / MS_DAY)
}

/** "AAAA-MM-DD" do número do dia. */
export function fromDayNumber(day: number): string {
  return new Date(day * MS_DAY).toISOString().slice(0, 10)
}

export function addDays(date: string, days: number): string {
  return fromDayNumber(dayNumber(date) + days)
}

/** Dias corridos de a até b (b − a). */
export function daysBetween(a: string, b: string): number {
  return dayNumber(b) - dayNumber(a)
}

/** Dia da semana ISO (1 = segunda … 7 = domingo). */
export function isoWeekday(date: string): number {
  const day = new Date(dayNumber(date) * MS_DAY).getUTCDay()
  return day === 0 ? 7 : day
}

/** Semana ISO do ano. */
export function isoWeek(date: string): number {
  const n = dayNumber(date)
  const thursday = n + 4 - isoWeekday(date)
  const year = new Date(thursday * MS_DAY).getUTCFullYear()
  return Math.floor((thursday - dayNumber(`${year}-01-01`)) / 7) + 1
}

export interface GanttAxis {
  from: string
  to: string
  scale: GanttScale
  dayWidth: number
  /** Dias no período (inclusive). */
  days: number
  width: number
}

export function axis(from: string, to: string, scale: GanttScale): GanttAxis {
  const days = Math.max(daysBetween(from, to) + 1, 1)
  const dayWidth = DAY_WIDTH[scale]
  return { from, to, scale, dayWidth, days, width: days * dayWidth }
}

/** x do início do dia. */
export function xOf(a: GanttAxis, date: string): number {
  return daysBetween(a.from, date) * a.dayWidth
}

/**
 * Barra de <c>start</c> a <c>end</c> (inclusive) cortada no período: x, largura (pelo menos 2 px) e se foi cortada em cada ponta; null fora
 * do período.
 */
export function barBox(a: GanttAxis, start: string, end: string): { x: number; width: number; clippedStart: boolean; clippedEnd: boolean } | null {
  const s = Math.max(daysBetween(a.from, start), 0)
  const e = Math.min(daysBetween(a.from, end) + 1, a.days)
  if (daysBetween(a.from, end) < 0 || daysBetween(a.from, start) >= a.days || e <= s) return null
  return { x: s * a.dayWidth, width: Math.max((e - s) * a.dayWidth, 2), clippedStart: daysBetween(a.from, start) < 0, clippedEnd: daysBetween(a.from, end) + 1 > a.days }
}

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']

export interface GanttTick {
  date: string
  x: number
  label: string
  /** Marca maior (segunda na escala Dia, primeiro do mês na Semana). */
  major: boolean
}

/**
 * Marcas do eixo: Dia = cada dia ("02") com a segunda maior; Semana = cada segunda ("S45 · 02/11"); Mês = o dia 1 de cada mês ("nov/26").
 */
export function ticks(a: GanttAxis): GanttTick[] {
  const out: GanttTick[] = []
  for (let i = 0; i < a.days; i++) {
    const date = addDays(a.from, i)
    const [y, m, d] = date.split('-')
    if (a.scale === 'dia') out.push({ date, x: i * a.dayWidth, label: d, major: isoWeekday(date) === 1 })
    else if (a.scale === 'semana' && isoWeekday(date) === 1) out.push({ date, x: i * a.dayWidth, label: `S${isoWeek(date)} · ${d}/${m}`, major: d === '01' || Number(d) <= 7 })
    else if (a.scale === 'mes' && d === '01') out.push({ date, x: i * a.dayWidth, label: `${MONTHS[Number(m) - 1]}/${y.slice(2)}`, major: m === '01' })
  }

  return out
}

/** Rótulo do mês para o topo do eixo na escala Dia ("novembro de 2026"). */
export function monthLabel(date: string): string {
  const [y, m] = date.split('-').map(Number)
  return new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, 15)))
}
