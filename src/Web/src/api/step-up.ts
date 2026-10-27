/**
 * "2FA de novo" (D6; BNK-01a CA 1): o servidor recusa a ação sensível com 403 two_factor_required quando o token não traz a prova
 * recente do código. O mutator chama {@link requestStepUp}; o diálogo único (StepUpHost) pede o código, troca só o access token e
 * devolve true para a mesma requisição ser repetida. Sem diálogo montado, ou cancelado, a recusa segue para quem chamou.
 */
export const TWO_FACTOR_REQUIRED = 'two_factor_required'
export const STEP_UP_URL = '/api/v1/me/2fa/step-up'
export const STEP_UP_CONFIRM = 'Para continuar, confirme o código da verificação em duas etapas.'
export const STEP_UP_ENROLL = 'Para continuar, ative a verificação em duas etapas.'

type Handler = (enrolled: boolean) => Promise<boolean>

let handler: Handler | null = null
let pending: Promise<boolean> | null = null

export function setStepUpHandler(next: Handler | null): void {
  handler = next
}

/** Um passo por vez: várias recusas juntas esperam a mesma confirmação. */
export function requestStepUp(enrolled: boolean): Promise<boolean> {
  if (!handler) return Promise.resolve(false)
  pending ??= handler(enrolled).finally(() => {
    pending = null
  })
  return pending
}
