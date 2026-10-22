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
  if (!token) {
    ephemeral = false
    deadline = null
  }
  listeners.forEach((notify) => notify())
}

/**
 * Como a sessão foi aberta (claim <c>amr</c> do access token, ADR 0008 adendo): "pwd", "otp" ou "pin". Só lê o payload do token em
 * memória para decidir a tela (o servidor confere tudo a cada request).
 */
export function sessionMethods(token: string | null = accessToken): string[] {
  if (!token) return []
  try {
    const payload = token.split('.')[1] ?? ''
    const text = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '='))
    const amr = (JSON.parse(text) as { amr?: unknown }).amr
    return Array.isArray(amr) ? amr.filter((m): m is string => typeof m === 'string') : typeof amr === 'string' ? [amr] : []
  } catch {
    return []
  }
}

/** Sessão aberta por PIN no aparelho da fábrica (ADR 0026 §3): só o /fab, com saída por inatividade. */
export const isPinSession = (token: string | null = accessToken) => sessionMethods(token).includes('pin')

let ephemeral = false

/**
 * Login completo feito no tablet (registrar aparelho, criar PIN, ADR 0026 §2): sem refresh; o token de 15 min vive só em memória e
 * a sessão acaba ao fechar o fluxo.
 */
export function markEphemeralSession(on: boolean): void {
  ephemeral = on
}

export const isEphemeralSession = () => ephemeral && !!accessToken

let deadline: number | null = null

/** Fim absoluto da sessão por PIN (12 h, sem renovação além disso): o /fab sai sozinho nessa hora. */
export function setSessionDeadline(iso: string | null): void {
  deadline = iso ? Date.parse(iso) : null
}

export const sessionDeadline = () => deadline

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
