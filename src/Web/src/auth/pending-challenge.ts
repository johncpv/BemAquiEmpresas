import { homeRouteFor } from './home-route'

/** Passo de 2FA entre a senha e a sessão (FND-05): fica só em memória, nunca no armazenamento do navegador. */
export interface PendingChallenge {
  challenge: string
  purpose: 'verify' | 'setup'
  expiresAt: string
  volta?: string
  /** Login no tablet da fábrica: a sessão do 2FA também não persiste (PRD-03b CA 6). */
  ephemeral?: boolean
}

let current: PendingChallenge | null = null

export const pendingChallenge = {
  get: () => current,
  set: (value: PendingChallenge | null) => {
    current = value
  },
}

/**
 * Rota segura para voltar depois de entrar (sem open redirect; PAT-01b CA 8): só caminho relativo do próprio app, em `/app`, `/fab` ou
 * `/b` (a URL do QR da etiqueta). URL absoluta, outro host, `//`, barra invertida, esquema ou caractere de controle: a página inicial
 * (DSH-04 CA 8).
 */
export function isSafeReturn(volta: string | undefined): volta is string {
  // Barra invertida e caracteres de controle (o navegador trata "/\" como "//"): fora.
  return !!volta && volta.length <= 2000 && /^\/(app|fab|b)(?:[/?#]|$)/.test(volta) && !volta.includes('//') && ![...volta].some((c) => c === '\\' || c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127)
}

export function safeReturn(volta: string | undefined): string {
  return isSafeReturn(volta) ? volta : homeRouteFor()
}
