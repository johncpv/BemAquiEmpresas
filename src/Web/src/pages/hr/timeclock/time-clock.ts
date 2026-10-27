import type { TimeEntrySource, TimeEntryType } from '@/api/generated/model'

/** Faixa fixa das telas do ponto (RH-01a CA 1): a mesma do servidor. */
export const INTERNAL_CONTROL_BANNER = 'Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).'

/** CA 8: sem rede não existe marcação (P-23). */
export const TIME_CLOCK_OFFLINE = 'Sem conexão. O ponto precisa de rede para usar a hora do servidor. Tente de novo ou avise o RH.'

export const NOT_LINKED = 'Seu usuário não está ligado a um funcionário ativo. Fale com o RH.'

export const OUT_OF_SEQUENCE_HINT = 'Esqueceu uma marcação? Peça o ajuste ao RH.'

/** Os quatro tipos na ordem da sequência sugerida (Entrada → Saída para o intervalo → Volta do intervalo → Saída). */
export const ENTRY_TYPES: readonly { value: TimeEntryType; label: string; verb: string }[] = [
  { value: 'In', label: 'Entrada', verb: 'Registrar entrada' },
  { value: 'BreakStart', label: 'Saída para o intervalo', verb: 'Registrar saída para o intervalo' },
  { value: 'BreakEnd', label: 'Volta do intervalo', verb: 'Registrar volta do intervalo' },
  { value: 'Out', label: 'Saída', verb: 'Registrar saída' },
]

export const entryLabel = (type: TimeEntryType) => ENTRY_TYPES.find((t) => t.value === type)?.label ?? type

/** Origem (CA 12): "App", "PIN no Tablet Linha 1" ou "Manual". */
export function sourceLabel(source: TimeEntrySource, deviceName?: string | null): string {
  if (source === 'Pin') return deviceName ? `PIN no ${deviceName}` : 'PIN no tablet'
  if (source === 'Manual') return 'Manual'
  return deviceName ? `App no ${deviceName}` : 'App'
}

/** Dicas das telas do ponto (Ajuda: "Hora do servidor", "Sem localização" e "Controle interno"). */
export const TIME_CLOCK_DICAS = {
  horaServidor: 'O horário da marcação é sempre o do servidor do BemAqui, nunca o do aparelho. Mudar o relógio do celular não muda o ponto.',
  semLocalizacao: 'A localização estava ligada, mas o aparelho não a enviou (permissão negada ou erro). A marcação vale do mesmo jeito.',
  controleInterno: 'O ponto do app é um controle interno da empresa para o espelho, o banco de horas e a folha. Ele não é o registro legal de ponto (REP-P, Portaria 671).',
} as const

const time = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit' })
const clockFormat = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit', second: '2-digit' })
const longDay = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long' })

/** Hora local (America/Sao_Paulo) de um instante UTC: "08:02". */
export const formatTime = (value: string | Date) => time.format(typeof value === 'string' ? new Date(value) : value)

/** Relógio do servidor (CA 2): a diferença entre a hora do servidor e a do aparelho é guardada uma vez e somada a cada segundo. */
export const serverOffset = (serverNow: string, deviceNow = Date.now()) => new Date(serverNow).getTime() - deviceNow
export const serverClock = (offset: number, deviceNow = Date.now()) => clockFormat.format(new Date(deviceNow + offset))

/** "terça-feira, 21 de outubro" de uma data de negócio (yyyy-mm-dd), sem mudar o dia pelo fuso. */
export function dayLabel(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  const text = longDay.format(new Date(Date.UTC(y, m - 1, d, 12)))
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** Agrupa por dia (mais recente primeiro), mantendo a ordem de cada dia. */
export function groupByDay<T extends { day: string }>(items: readonly T[]): { day: string; items: T[] }[] {
  const groups = new Map<string, T[]>()
  for (const item of items) groups.set(item.day, [...(groups.get(item.day) ?? []), item])
  return [...groups.entries()].sort(([a], [b]) => b.localeCompare(a)).map(([day, list]) => ({ day, items: list }))
}

/** Localização reduzida para mostrar: "-23,5505, -46,6333 (± 19 m)". */
export function locationText(lat?: number | null, lng?: number | null, accuracy?: number | null): string {
  if (lat == null || lng == null) return 'Sem localização'
  const n = (v: number) => v.toLocaleString('pt-BR', { minimumFractionDigits: 4, maximumFractionDigits: 4 })
  return `${n(lat)}, ${n(lng)}${accuracy != null ? ` (± ${accuracy} m)` : ''}`
}

/** Localização do navegador para a marcação (CA 9): nunca bloqueia; negada, erro ou demora viram "Sem localização". */
export function currentPosition(timeoutMs = 8000): Promise<{ latitude: number; longitude: number; accuracyM: number } | null> {
  if (typeof navigator === 'undefined' || !navigator.geolocation) return Promise.resolve(null)
  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracyM: p.coords.accuracy }),
      () => resolve(null),
      { enableHighAccuracy: false, timeout: timeoutMs, maximumAge: 60_000 },
    )
  })
}

/** Período padrão da consulta do RH: os últimos 7 dias (yyyy-mm-dd, no dia de São Paulo). */
export function defaultRange(today: string): { from: string; to: string } {
  const [y, m, d] = today.split('-').map(Number)
  const from = new Date(Date.UTC(y, m - 1, d - 6, 12)).toISOString().slice(0, 10)
  return { from, to: today }
}

const isoDay = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' })

/** Hoje em São Paulo (yyyy-mm-dd). */
export const todayInSaoPaulo = (now = new Date()) => isoDay.format(now)
