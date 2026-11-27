import type { StatusTone } from '@/components/ui/status-chip'
import { formatInstantDate } from '@/lib/format'

/** Situação da entrega de um aviso (INT-02): Entregue ok, Tentando de novo alerta, Parado crítico, Retido e Ignorado informativos. */
export const DELIVERY_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Pending: { label: 'Na fila', tone: 'info' },
  Retrying: { label: 'Tentando de novo', tone: 'warn' },
  Delivered: { label: 'Entregue', tone: 'ok' },
  DeadLettered: { label: 'Parado', tone: 'crit' },
  Held: { label: 'Retido', tone: 'info' },
  Skipped: { label: 'Ignorado', tone: 'info' },
}

export const DELIVERY_STATUS_OPTIONS = Object.entries(DELIVERY_STATUS).map(([value, s]) => ({ value, label: s.label === 'Parado' ? 'Parados' : s.label }))

export const HELD_HINT = 'Retido: a conexão com o Conecta está suspensa. O aviso espera e não é enviado até a conexão voltar.'
export const SKIPPED_HINT = 'Ignorado: o aviso não foi enviado porque a conexão acabou ou o Conecta não tem mais permissão para ver esse tipo de dado. O motivo aparece na linha.'
export const STALLED_HINT = 'O aviso falhou 8 vezes em cerca de 2 dias. Confira se o Conecta está no ar e clique em Reenviar.'
export const PURGED_HINT = 'O conteúdo foi apagado depois de 90 dias'

/** Resumo de saúde (CA 19): chip ok quando nada falha; senão crítico, com a data desde quando. */
export function healthState(summary: { connected: boolean; healthy: boolean; failingSince?: string | null }): { tone: StatusTone; text: string } | null {
  if (!summary.connected) return null
  if (summary.healthy) return { tone: 'ok', text: 'Os avisos estão chegando ao Conecta' }
  const since = summary.failingSince ? formatInstantDate(summary.failingSince).slice(0, 5) : null
  return { tone: 'crit', text: since ? `O Conecta não está recebendo os avisos desde ${since}` : 'O Conecta não está recebendo os avisos' }
}

/** Aba de ajuda pela aba da tela de eventos. */
export const EVENTS_HELP_TAB: Record<string, string> = { recebidos: 'integracoes-eventos', enviados: 'integracoes-eventos-enviados' }
