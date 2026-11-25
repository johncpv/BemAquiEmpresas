/**
 * Janela máxima sem sincronizar (OFF-03 CA 9, 10 e 14; PP-10). A conta parte do **último horário recebido do servidor**, nunca só do relógio do
 * aparelho, e o "agora" nunca volta atrás:
 * - o **piso** é o maior entre o último horário do servidor e o maior horário que o aparelho já viu (guardado a cada consulta);
 * - relógio do aparelho antes do piso (atrasado de propósito ou não): vale o piso, a divergência é registrada e o caixa não abre turno novo até
 *   sincronizar. Atrasar o relógio não estende a janela;
 * - a 80% da janela, a faixa fica em alerta com o prazo ("Conecte à internet até amanhã às 14h."); vencida, nenhum turno novo abre (o aberto fecha).
 */
export const CLOCK_META = 'offline.clock'

/** Folga do relógio do aparelho (os 2 min da ADR 0026). */
export const CLOCK_TOLERANCE_MS = 2 * 60_000
export const WARN_FRACTION = 0.8
const HOUR = 3_600_000

export interface OfflineClock {
  /** Último horário (ISO) que veio do servidor: foto ou delta do catálogo, ou resposta da subida. É o início da janela. */
  serverAt: string | null
  /** Maior horário do aparelho (ISO) já visto: o "agora" nunca volta para antes dele. */
  highWater: string | null
}

export type WindowState =
  | { kind: 'unknown' }
  | { kind: 'ok'; deadline: Date }
  | { kind: 'warn'; deadline: Date }
  | { kind: 'expired'; deadline: Date; days: number }
  | { kind: 'clock-behind'; floor: Date }

const ms = (iso: string | null) => (iso ? new Date(iso).getTime() : Number.NaN)

/** O piso do "agora": o maior entre o horário do servidor e o maior já visto no aparelho (NaN sem nenhum). */
export function floorOf(clock: OfflineClock | null | undefined): number {
  const values = [ms(clock?.serverAt ?? null), ms(clock?.highWater ?? null)].filter((v) => Number.isFinite(v))
  return values.length ? Math.max(...values) : Number.NaN
}

/** O relógio do aparelho voltou para antes do piso (além da folga)? */
export function clockBehind(clock: OfflineClock | null | undefined, deviceNow: Date): boolean {
  const floor = floorOf(clock)
  return Number.isFinite(floor) && deviceNow.getTime() < floor - CLOCK_TOLERANCE_MS
}

/** Estado da janela agora. Sem horário do servidor ou sem a janela da loja: `unknown` (o aparelho nunca sincronizou; a primeira abertura pede rede). */
export function windowState(clock: OfflineClock | null | undefined, windowHours: number | null | undefined, deviceNow: Date): WindowState {
  if (!clock?.serverAt || !windowHours || windowHours <= 0) return { kind: 'unknown' }
  const floor = floorOf(clock)
  if (clockBehind(clock, deviceNow)) return { kind: 'clock-behind', floor: new Date(floor) }
  const now = Math.max(deviceNow.getTime(), floor)
  const start = ms(clock.serverAt)
  const span = Math.min(windowHours, 168) * HOUR
  const deadline = new Date(start + span)
  const elapsed = now - start
  if (elapsed >= span) return { kind: 'expired', deadline, days: Math.max(1, Math.floor(elapsed / (24 * HOUR))) }
  return elapsed >= span * WARN_FRACTION ? { kind: 'warn', deadline } : { kind: 'ok', deadline }
}

/** "às 14h" ou "às 14h30". */
const hourText = (d: Date) => (d.getMinutes() === 0 ? `${d.getHours()}h` : `${d.getHours()}h${String(d.getMinutes()).padStart(2, '0')}`)

const dayKey = (d: Date) => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`

/** "hoje às 14h", "amanhã às 14h" ou "27/11 às 14h". */
export function deadlineText(deadline: Date, now: Date): string {
  const tomorrow = new Date(now)
  tomorrow.setDate(now.getDate() + 1)
  const day = dayKey(deadline) === dayKey(now) ? 'hoje'
    : dayKey(deadline) === dayKey(tomorrow) ? 'amanhã'
    : deadline.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })
  return `${day} às ${hourText(deadline)}`
}

/** CA 10: o alerta da faixa a 80% da janela. */
export const warnMessage = (deadline: Date, now: Date) => `Conecte à internet até ${deadlineText(deadline, now)}.`

/** CA 9: a janela venceu; o turno aberto pode ser fechado, um novo não abre. */
export const expiredMessage = (days: number) =>
  `Este caixa está há ${days} ${days === 1 ? 'dia' : 'dias'} sem enviar as vendas. Conecte à internet para abrir o caixa.`

/** CA 14: relógio atrasado em relação ao último horário conhecido. */
export const CLOCK_BEHIND_MESSAGE = 'O relógio deste aparelho está atrasado em relação ao último horário do servidor. Acerte o relógio e conecte à internet para abrir o caixa.'

/** O que impede abrir um turno novo agora (null: pode abrir). */
export function openBlockedMessage(state: WindowState): string | null {
  if (state.kind === 'expired') return expiredMessage(state.days)
  if (state.kind === 'clock-behind') return CLOCK_BEHIND_MESSAGE
  return null
}

/** O piso guardado avança com o horário do servidor ou com o maior horário visto (nunca volta). */
export function advanceClock(clock: OfflineClock | null | undefined, input: { serverAt?: string | null; deviceNow?: Date }): OfflineClock {
  const next: OfflineClock = { serverAt: clock?.serverAt ?? null, highWater: clock?.highWater ?? null }
  if (input.serverAt && (!next.serverAt || ms(input.serverAt) > ms(next.serverAt))) next.serverAt = input.serverAt
  // O aparelho só eleva o maior horário visto quando o relógio não está atrás do piso (senão, o piso fica e a divergência é registrada).
  if (input.deviceNow && !clockBehind(next, input.deviceNow) && (!next.highWater || input.deviceNow.getTime() > ms(next.highWater))) next.highWater = input.deviceNow.toISOString()
  return next
}
