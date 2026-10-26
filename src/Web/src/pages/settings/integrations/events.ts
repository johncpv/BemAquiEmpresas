import type { StatusTone } from '@/components/ui/status-chip'

/** Situação do evento recebido (FND-16): Recebido info, Processado ok, Ignorado neutro, Falhou alerta, Parado crítico. */
export const EVENT_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Received: { label: 'Recebido', tone: 'info' },
  Processed: { label: 'Processado', tone: 'ok' },
  Ignored: { label: 'Ignorado', tone: 'neutral' },
  Failed: { label: 'Falhou', tone: 'warn' },
  DeadLettered: { label: 'Parado', tone: 'crit' },
}

export const STATUS_OPTIONS = Object.entries(EVENT_STATUS).map(([value, s]) => ({ value, label: s.label === 'Parado' ? 'Parados' : s.label }))

export const STALLED_HINT = 'O aviso falhou 10 vezes. Corrija a causa e clique em Reprocessar.'
export const CONTENT_HINT = 'Os dados pessoais aparecem mascarados.'

/**
 * Propositivo (FND-16): com algum evento parado e nenhuma situação escolhida na URL, a tela abre no filtro "Parados". Só na
 * primeira carga: depois de a pessoa escolher "Todos", a escolha dela vale.
 */
export function defaultStatus(current: string | undefined, deadLettered: number | undefined, alreadyApplied: boolean): string | undefined {
  if (alreadyApplied || current || !deadLettered) return undefined
  return 'DeadLettered'
}
