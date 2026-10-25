import { ApiError } from '@/api/http'

/** Mensagens da superfície /tv (PNL-01a, ADR 0030): a TV mostra o estado, nunca um erro técnico. */
export const TV_TEXT = {
  connecting: 'Conectando…',
  invalid: 'Link inválido ou desligado.',
  invalidHint: 'Peça um novo link a quem cuida do painel e abra-o no navegador desta TV.',
  legacy: 'Este link antigo foi desligado. Peça um novo link a quem cuida do painel.',
  reconnect: 'Esta tela precisa ser conectada de novo. Peça um novo link a quem cuida do painel.',
  unavailable: 'Painel indisponível nesta versão do sistema.',
  network: 'Esta tela só funciona na rede da fábrica.',
  live: 'Ao vivo',
  reconnecting: 'Reconectando…',
} as const

export const revokedText = (company: string) => `Esta tela foi desligada por ${company}.`

/** Estado da TV: o painel, ou uma das telas de aviso. */
export type TvState =
  | { kind: 'connecting' }
  | { kind: 'panel' }
  | { kind: 'invalid' }
  | { kind: 'legacy' }
  | { kind: 'reconnect' }
  | { kind: 'revoked'; company: string }
  | { kind: 'unavailable' }
  | { kind: 'network' }

/**
 * Lê o token do fragmento (/tv#{token}) e apaga o fragmento da barra na hora (history.replaceState), antes de qualquer chamada:
 * o fragmento não vai ao servidor, ao log nem ao Referer (ADR 0030 §1).
 */
export function takeFragmentToken(location: Pick<Location, 'hash' | 'pathname'>, history: Pick<History, 'replaceState' | 'state'>): string | null {
  const token = location.hash.startsWith('#') ? location.hash.slice(1).trim() : ''
  if (location.hash) history.replaceState(history.state, '', location.pathname)
  return token.length > 0 ? token : null
}

/** Resposta da API do quiosque → estado da TV. */
export function stateOfError(error: unknown, fallbackCompany: string): TvState {
  if (!(error instanceof ApiError)) return { kind: 'reconnect' }
  switch (error.code) {
    case 'kiosk_revoked':
      return { kind: 'revoked', company: typeof error.data.companyName === 'string' && error.data.companyName ? error.data.companyName : fallbackCompany }
    case 'kiosk_network':
      return { kind: 'network' }
    case 'module_not_released':
    case 'tenant_suspended':
      return { kind: 'unavailable' }
    case 'kiosk_link_invalid':
      return { kind: 'invalid' }
    default:
      return { kind: 'reconnect' }
  }
}

/** Mensagem de controle do hub (KioskRevoked) → estado da TV. */
export function stateOfEnd(reason: string | undefined, company: string): TvState {
  if (reason === 'revoked') return { kind: 'revoked', company }
  if (reason === 'unavailable') return { kind: 'unavailable' }
  return { kind: 'reconnect' }
}

/** Hora local "hh:mm" (America/Sao_Paulo) para o relógio e o "Atualizado às". */
export function clockText(date: Date, seconds = false): string {
  return new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', ...(seconds ? { second: '2-digit' } : {}), timeZone: 'America/Sao_Paulo' }).format(date)
}
