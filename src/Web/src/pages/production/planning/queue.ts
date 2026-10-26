import type { PlanningQueueChangeKind, PlanningQueueChangeView, PlanningQueuePreviewRow, PlanningQueueRow } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Dicas da aba Fila (PLN-02): "Folga" e "Prévia". */
export const QUEUE_DICAS = {
  folga: 'Dias corridos entre a data prometida ao cliente e a entrega prevista pelo plano. Positiva: sobra prazo. Negativa: o projeto vai atrasar esse tanto.',
  previa: 'Simulação do plano com a ordem nova, antes de salvar: mostra a entrega prevista de cada projeto antes → depois. Nada muda na fábrica até você clicar em "Salvar ordem".',
} as const

export const EDIT_BANNER = 'Ordem alterada, ainda não salva.'
export const SAVED_MESSAGE = 'Ordem salva. Recalculando as datas…'
export const READ_ONLY_NOTE = 'Para mudar a ordem, peça a quem cuida da produção.'

/** A ordem com o item de <paramref name="from"/> levado para <paramref name="to"/> (índices; fora da lista não muda nada). */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const list = [...items]
  if (from < 0 || from >= list.length || to < 0 || to >= list.length || from === to) return list
  const [item] = list.splice(from, 1)
  list.splice(to, 0, item)
  return list
}

export const sameOrder = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((id, i) => id === b[i])

/** Anúncio do leitor de tela (acessibilidade): "Projeto {n} movido para a posição {p}". */
export function movedAnnouncement(number: string, position: number): string {
  return `Projeto ${number} movido para a posição ${position}`
}

/** Chip do prazo (CA 1): Atrasado, Em risco, Sem previsão ou No prazo, pela função única do servidor. */
export function queueChip(row: Pick<PlanningQueueRow, 'risk' | 'hasForecast'>): { tone: StatusTone; label: string } {
  if (row.risk === 'Late') return { tone: 'crit', label: 'Atrasado' }
  if (row.risk === 'AtRisk') return { tone: 'warn', label: 'Em risco' }
  if (!row.hasForecast) return { tone: 'neutral', label: 'Sem previsão' }
  return { tone: 'ok', label: 'No prazo' }
}

/** "05/12" (dia e mês) para a prévia em duas linhas. */
export function shortDate(value: string | null | undefined): string {
  if (!value) return '—'
  const [, m, d] = value.split('-')
  return `${d}/${m}`
}

/** Folga em dias com sinal: "+3 dias", "−7 dias", "0 dia"; sem previsão, "—". */
export function slackShort(slack: number | null | undefined): string {
  if (slack === null || slack === undefined) return '—'
  const n = Math.abs(slack)
  const unit = n === 1 || n === 0 ? 'dia' : 'dias'
  return slack > 0 ? `+${n} ${unit}` : slack < 0 ? `−${n} ${unit}` : `0 ${unit}`
}

/** "05/12 → 09/12" (o mesmo dia aparece uma vez). */
export function previewDates(row: Pick<PlanningQueuePreviewRow, 'forecastBefore' | 'forecastAfter'>): string {
  const before = shortDate(row.forecastBefore)
  const after = shortDate(row.forecastAfter)
  return before === after ? before : `${before} → ${after}`
}

/** CA 5: "Estes projetos passam a atrasar: PRJ-1 (7 dias depois do prometido), PRJ-2 (1 dia depois do prometido)." */
export function worsenedLine(rows: readonly PlanningQueuePreviewRow[], numberOf: (projectId: string) => string): string | null {
  const worse = rows.filter((r) => r.worsened)
  if (worse.length === 0) return null
  const parts = worse.map((r) => {
    const n = r.daysLate ?? 0
    return `${numberOf(r.projectId)} (${n === 1 ? '1 dia' : `${n} dias`} depois do prometido)`
  })
  return `Estes projetos passam a atrasar: ${parts.join(', ')}.`
}

const KIND_LABEL: Record<PlanningQueueChangeKind, string> = {
  Initial: 'Fila montada',
  Manual: 'Reordenada',
  Entered: 'Entrou',
  Left: 'Saiu',
}

export const changeKindLabel = (kind: PlanningQueueChangeKind) => KIND_LABEL[kind]

/** Posições antes → depois de uma mudança: "PRJ-1: 3 → 1", "PRJ-2: entrou na 2", "PRJ-3: saiu da 4". */
export function changeMoves(change: PlanningQueueChangeView, limit = 6): string {
  const parts = change.moves.slice(0, limit).map((m) => {
    if (m.from === null || m.from === undefined) return `${m.projectNumber}: entrou na ${m.to}`
    if (m.to === null || m.to === undefined) return `${m.projectNumber}: saiu da ${m.from}`
    return `${m.projectNumber}: ${m.from} → ${m.to}`
  })
  const more = change.moves.length - limit
  return more > 0 ? `${parts.join('; ')}; e mais ${more}` : parts.join('; ')
}
