/** Passo de 2FA entre a senha e a sessão (FND-05): fica só em memória, nunca no armazenamento do navegador. */
export interface PendingChallenge {
  challenge: string
  purpose: 'verify' | 'setup'
  expiresAt: string
  volta?: string
}

let current: PendingChallenge | null = null

export const pendingChallenge = {
  get: () => current,
  set: (value: PendingChallenge | null) => {
    current = value
  },
}

/** Rota segura para voltar depois de entrar: só caminhos internos do app (sem open redirect). */
export function safeReturn(volta: string | undefined): string {
  return volta && volta.startsWith('/') && !volta.startsWith('//') && !volta.startsWith('/entrar') ? volta : '/app'
}
