import { refreshSession } from '@/api/generated/bemaqui'
import { getAccessToken, setAccessToken } from '@/api/session'

let pending: Promise<boolean> | null = null

/**
 * Antes de abrir o backoffice: já tem token em memória, ou retoma a sessão pelo cookie de refresh (HttpOnly). Sem
 * sessão, o guarda de rota leva ao login guardando a rota (FND-04, FND-09c CA 3).
 */
export function ensureSession(): Promise<boolean> {
  if (getAccessToken()) return Promise.resolve(true)
  pending ??= refreshSession()
    .then((r) => {
      setAccessToken(r.accessToken)
      return true
    })
    .catch(() => false)
    .finally(() => {
      pending = null
    })
  return pending
}
