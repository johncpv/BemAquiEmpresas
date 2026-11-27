import type { StatusTone } from '@/components/ui/status-chip'

/** Descrição do módulo para a tela "não liberada" (o Conecta fica fora do menu, INT-01 CA 2). */
export const CONECTA_MODULE = { name: 'Conecta', description: 'Liga o ERP ao Conecta: vender, cobrar e avisar o cliente pelo WhatsApp.' }

export type ConnectionAction = 'disconnect' | 'reauthorize' | 'connect'

/** Chip, tom e ação de cada situação da conexão (INT-01 CA 20). Sem conexão, "Conectar". */
export function connectionState(connection: { status: string; reason: string } | null | undefined): { tone: StatusTone; action: ConnectionAction } {
  if (!connection) return { tone: 'info', action: 'connect' }
  if (connection.status === 'Active') return { tone: 'ok', action: 'disconnect' }
  if (connection.status === 'Suspended') return { tone: 'warn', action: 'reauthorize' }
  return { tone: connection.reason === 'CredentialReuse' ? 'crit' : 'info', action: 'connect' }
}

/** Mensagem de cada erro da tela de autorizar (CA 8), pelo código do problema. */
export function authorizeErrorText(code: string | undefined, fallback?: string): string {
  if (code === 'auth.2fa_invalid_code') return 'Código incorreto. Confira no aplicativo autenticador e tente de novo.'
  if (code === 'connect_intent_invalid' || code === 'connect_intent_used') return 'Este pedido de conexão não foi iniciado por você neste ERP.'
  return fallback ?? 'Não foi possível autorizar agora. Tente de novo.'
}

/** O pedido do aplicativo, como chegou na URL do /oauth/authorize (repassado sem alterar, CA 3). */
export function authorizeRequestFrom(search: URLSearchParams) {
  const get = (name: string) => search.get(name)
  return {
    responseType: get('response_type'),
    clientId: get('client_id'),
    redirectUri: get('redirect_uri'),
    scope: get('scope'),
    state: get('state'),
    codeChallenge: get('code_challenge'),
    codeChallengeMethod: get('code_challenge_method'),
    intent: get('intent'),
    accountLabel: get('account_label'),
  }
}

/** Sai do ERP (para o Conecta ou de volta a ele). Isolado para os testes. */
export const browser = {
  assign: (url: string) => window.location.assign(url),
  search: () => window.location.search,
}
