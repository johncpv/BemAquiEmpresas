import type { FactoryLoadBand, FactoryLoadCell, FactoryLoadRow, FactoryLoadWeek, FactoryLoadWeekly } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { hoursText } from './planning'

/** Dicas da aba Ocupação (PLN-04): "Comprometidas", "Capacidade", "Lotado", "Acima da capacidade" e "Depois de". */
export const LOAD_DICAS = {
  comprometidas: 'Horas que o plano já reservou no centro nos dias da semana, somando todas as etapas. Na semana atual, contam só os dias a partir de hoje.',
  capacidade: 'Horas que o centro tem nos mesmos dias, pelo calendário, feriados e paradas, lidas agora. Férias de funcionário não reduzem.',
  lotado: 'A semana tem 95% ou mais das horas do centro tomadas. Abra a célula para ver as etapas da semana.',
  acima: 'As horas planejadas passaram da capacidade porque o calendário mudou depois do último cálculo (feriado ou parada novos). Recalcule o plano para redistribuir.',
  depoisDe: 'Carga já planejada depois da última semana mostrada. O plano não passa de 100%: o que não cabe vai para as semanas seguintes, e aparece aqui.',
} as const

export const MIN_WEEKS = 1
export const MAX_WEEKS = 26
export const DEFAULT_WEEKS = 8
export const WEEK_OPTIONS = [4, 5, 8, 12, 26] as const

export type LoadView = 'tabela' | 'grafico'

/** Filtros da aba na URL (CA 9): ?semanas=, ?linha=, ?centro=, ?visao= e ?semana= (a 400 px). */
export interface LoadFilters {
  weeks: number
  line?: string
  center?: string
  view: LoadView
  week?: string
}

export function parseFilters(search: Record<string, unknown>): LoadFilters {
  const raw = Number(search.semanas)
  const weeks = Number.isInteger(raw) && raw >= MIN_WEEKS && raw <= MAX_WEEKS ? raw : DEFAULT_WEEKS
  const text = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined)
  return { weeks, line: text(search.linha), center: text(search.centro), view: search.visao === 'grafico' ? 'grafico' : 'tabela', week: text(search.semana) }
}

/** O que vai para a URL: só o que difere do padrão. */
export function filtersToSearch(f: LoadFilters): Record<string, string | number | undefined> {
  return { semanas: f.weeks === DEFAULT_WEEKS ? undefined : f.weeks, linha: f.line, centro: f.center, visao: f.view === 'tabela' ? undefined : f.view, semana: f.week }
}

/** "S45 · 02/11" (semana ISO e a segunda-feira). */
export function weekLabel(week: Pick<FactoryLoadWeek, 'isoWeek' | 'start'>): string {
  return `S${week.isoWeek} · ${formatDate(week.start).slice(0, 5)}`
}

/** "31,3%" com 1 casa; capacidade 0 → "—". */
export function percentText(percent: number | null | undefined): string {
  return percent === null || percent === undefined ? '—' : `${percent.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
}

/** "10 de 32 h". */
export function hoursOf(cell: Pick<FactoryLoadCell, 'committedHours' | 'capacityHours'>): string {
  return `${hoursText(cell.committedHours).replace(/ h$/, '')} de ${hoursText(cell.capacityHours)}`
}

/** CA 6 e 4: o tom e o texto do estado (o texto sempre junto da cor); de 60% a menos de 95% sem destaque. */
export function bandChip(band: FactoryLoadBand): { tone: StatusTone; label: string } | null {
  switch (band) {
    case 'Idle':
      return { tone: 'info', label: 'Folga' }
    case 'Full':
      return { tone: 'warn', label: 'Lotado' }
    case 'Over':
      return { tone: 'crit', label: 'Acima da capacidade' }
    default:
      return null
  }
}

/** "19/02" no ano de hoje; "19/02/2027" em outro ano. */
export function shortDate(date: string, today: string): string {
  const full = formatDate(date)
  return date.slice(0, 4) === today.slice(0, 4) ? full.slice(0, 5) : full
}

/** O último dia da última semana mostrada: o título do quadro "Depois de {data}". */
export function horizonEnd(load: FactoryLoadWeekly): string | null {
  return load.weeks.at(-1)?.end ?? null
}

/** CA 3: os centros com carga depois da última semana, do mais ocupado para o menos. */
export function afterRows(rows: FactoryLoadRow[]): FactoryLoadRow[] {
  return rows.filter((r) => r.hoursAfter > 0).sort((a, b) => (b.busyUntil ?? '').localeCompare(a.busyUntil ?? '') || b.hoursAfter - a.hoursAfter)
}

/** "Corte ocupado até 19/02" (o centro com o último dia planejado mais distante). */
export function busyLine(rows: FactoryLoadRow[], today: string): string | null {
  const last = rows.filter((r) => r.busyUntil).sort((a, b) => b.busyUntil!.localeCompare(a.busyUntil!))[0]
  return last ? `${last.name} ocupado até ${shortDate(last.busyUntil!, today)}` : null
}

/** CA 8: "Semana mais ocupada: {semana}, {x}%. Corte ocupado até {data}." */
export function summaryLine(load: FactoryLoadWeekly, row: FactoryLoadRow, busyRows: FactoryLoadRow[]): string {
  const best = row.cells.reduce<{ i: number; p: number } | null>((acc, c, i) => (c.percent !== null && (acc === null || c.percent > acc.p) ? { i, p: c.percent } : acc), null)
  const busy = busyLine(busyRows, load.today)
  if (!best || row.cells.every((c) => c.committedHours === 0)) {
    return busy ? `Nenhuma hora planejada nas semanas mostradas. ${busy}.` : 'Nenhuma hora planejada nas semanas mostradas.'
  }

  return `Semana mais ocupada: ${weekLabel(load.weeks[best.i])}, ${percentText(best.p)}.${busy ? ` ${busy}.` : ''}`
}

/** Dados do gráfico (CA 8): uma barra por semana com o percentual (capacidade 0 fica sem ponto). */
export function chartData(load: FactoryLoadWeekly, row: FactoryLoadRow): { semana: string; ocupacao: number | null }[] {
  return load.weeks.map((w, i) => ({ semana: weekLabel(w), ocupacao: row.cells[i]?.percent ?? null }))
}

/** Nenhuma hora no período nem depois: o estado vazio "Nenhuma etapa planejada no período.". */
export function isEmptyPlan(load: FactoryLoadWeekly): boolean {
  return load.factory.hoursAfter === 0 && load.factory.cells.every((c) => c.committedHours === 0)
}

/** Células "Acima da capacidade" (CA 4), para a faixa com "Recalcular agora". */
export function overCount(rows: FactoryLoadRow[]): number {
  return rows.reduce((n, r) => n + r.cells.filter((c) => c.band === 'Over').length, 0)
}

/** "Plano de 14:05" (hoje) ou "Plano de 24/10/2026 14:05". */
export function planLine(computedAt: string | null | undefined, today: string): string {
  if (!computedAt) return 'O plano ainda não foi calculado.'
  const at = new Date(computedAt)
  const day = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(at)
  const hhmm = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit' }).format(at)
  return day === today ? `Plano de ${hhmm}` : `Plano de ${formatDate(day)} ${hhmm}`
}

/** Rótulo acessível da célula: centro, semana, horas, percentual e estado. */
export function cellLabel(row: FactoryLoadRow, week: FactoryLoadWeek, cell: FactoryLoadCell): string {
  const chip = bandChip(cell.band)
  return `${row.name}, ${weekLabel(week)}: ${hoursOf(cell)}, ${percentText(cell.percent)}${chip ? `, ${chip.label}` : ''}`
}
