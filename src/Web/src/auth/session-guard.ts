import { renewSession } from '@/api/http'
import { getAccessToken } from '@/api/session'

/**
 * Antes de abrir o backoffice: já tem token em memória, ou retoma a sessão pelo cookie de refresh (HttpOnly). Sem
 * sessão, o guarda de rota leva ao login guardando a rota (FND-04, FND-09c CA 3). O refresh é o mesmo do app inteiro
 * (uma ida por vez, ver renewSession).
 */
export function ensureSession(): Promise<boolean> {
  if (getAccessToken()) return Promise.resolve(true)
  return renewSession()
}
