import type { AbsenceReasonOption, TimeAdjustmentStatus, TimeAdjustmentView } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Chip de situação do pedido (RH-01b): Aguardando (info), Aprovado (ok), Reprovado (crítico), Cancelado (neutro). */
export const STATUS_CHIP: Record<TimeAdjustmentStatus, { tone: StatusTone; label: string }> = {
  Pending: { tone: 'info', label: 'Aguardando aprovação' },
  Approved: { tone: 'ok', label: 'Aprovado' },
  Rejected: { tone: 'crit', label: 'Reprovado' },
  Cancelled: { tone: 'neutral', label: 'Cancelado' },
}

export const STATUS_FILTERS: readonly { value: TimeAdjustmentStatus | ''; label: string }[] = [
  { value: 'Pending', label: 'Aguardando aprovação' },
  { value: 'Approved', label: 'Aprovados' },
  { value: 'Rejected', label: 'Reprovados' },
  { value: 'Cancelled', label: 'Cancelados' },
  { value: '', label: 'Todos' },
]

export const isStatus = (value: unknown): value is TimeAdjustmentStatus => value === 'Pending' || value === 'Approved' || value === 'Rejected' || value === 'Cancelled'

export const JUSTIFICATION_MIN = 10
export const JUSTIFICATION_MAX = 200
export const NO_CID = 'Não informe CID nem diagnóstico.'
export const SELF_APPROVED = 'autoaprovado'

/** Justificativa de 10 a 200 caracteres (CA 2): a mensagem do campo, ou null. */
export function justificationError(text: string): string | null {
  const length = text.trim().length
  return length < JUSTIFICATION_MIN || length > JUSTIFICATION_MAX ? `Escreva a justificativa com ${JUSTIFICATION_MIN} a ${JUSTIFICATION_MAX} caracteres. ${NO_CID}` : null
}

/** "4 h", "1 h 30 min", "45 min" ou "o dia inteiro". */
export function durationText(minutes?: number | null): string {
  if (minutes == null) return 'o dia inteiro'
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h > 0 && m === 0) return `${h} h`
  return h > 0 ? `${h} h ${m} min` : `${m} min`
}

/** "04:30" para minutos; vazio para o dia inteiro. */
export function hoursToMinutes(text: string): number | null {
  const match = /^(\d{1,2}):([0-5]\d)$/.exec(text.trim())
  if (!match) return null
  const total = Number(match[1]) * 60 + Number(match[2])
  return total > 0 && total <= 24 * 60 ? total : null
}

/** Horas do dia em "8 h 50 min" (antes e depois, CA 10). */
export const workedText = (minutes: number) => (minutes === 0 ? '0 h' : durationText(minutes))

/** "21/10/2026" de uma data de negócio (yyyy-mm-dd). */
export function shortDay(day: string): string {
  const [y, m, d] = day.split('-')
  return `${d}/${m}/${y}`
}

/** Selecionáveis para "Aprovar selecionados" (CA 15): só os pendentes que a pessoa pode decidir. */
export const selectable = (items: readonly TimeAdjustmentView[]) => items.filter((a) => a.status === 'Pending' && a.canDecide)

/** Dicas das telas do ajuste (Ajuda: "Desconsiderar", "Abono" e "Motivo de saúde"). */
export const ADJUSTMENT_DICAS = {
  desconsiderar: 'A marcação continua guardada, só deixa de contar.',
  abono: 'O abono justifica uma ausência (o dia inteiro ou algumas horas). Aprovado, o espelho conta essas horas como justificadas; "Falta não justificada" é só registro.',
  saude: 'Atestado médico e acidente de trabalho são dados de saúde (LGPD). Só quem tem a permissão Ver dados de saúde vê o motivo, a justificativa e o documento; os outros veem "Ausência justificada".',
  autoaprovado: 'Aprovado pelo próprio Proprietário porque ele é o único que aprova ajustes no sistema.',
} as const

/** Último dia útil (segunda a sexta) antes de hoje: o padrão do "Lançar o dia pela jornada" (o dia de hoje ainda não terminou). */
export function lastWorkday(today: string): string {
  const [y, m, d] = today.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d - 1, 12))
  while (date.getUTCDay() === 0 || date.getUTCDay() === 6) date.setUTCDate(date.getUTCDate() - 1)
  return date.toISOString().slice(0, 10)
}

/** Motivos que valem para abono (CA 7); os de saúde só para quem pode (ou para o próprio funcionário). */
export const absenceReasons = (all: readonly AbsenceReasonOption[] | undefined, canHealth: boolean) =>
  (all ?? []).filter((r) => (r.appliesTo === 'Absence' || r.appliesTo === 'Both') && (canHealth || !r.isHealth))
