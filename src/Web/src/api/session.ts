/**
 * Sessão no navegador (FND-04 CA 3): o access token fica só em memória, nunca em localStorage, sessionStorage ou
 * cookie legível por JS. O refresh vive no cookie HttpOnly __Host-bq_rt, que o JS não lê.
 */
let accessToken: string | null = null
const listeners = new Set<() => void>()

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
  listeners.forEach((notify) => notify())
}

export function onSessionChange(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

let permVersion: string | null = null
let onPermVersionChange: (() => void) | null = null

/** FND-06c CA 15: o backend manda X-Perm-Version; mudou, o front recarrega /me/permissions sem novo login. */
export function trackPermVersion(version: string | null): void {
  if (!version) return
  if (permVersion !== null && version !== permVersion) onPermVersionChange?.()
  permVersion = version
}

export function setPermVersionListener(listener: (() => void) | null): void {
  onPermVersionChange = listener
}
