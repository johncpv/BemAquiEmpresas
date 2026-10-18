import { getAccessToken, setAccessToken, trackPermVersion } from './session'

/** Erro da API no formato ProblemDetails (RFC 9457) com code e errors por campo (FND-07). */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fields: Record<string, string[]>

  constructor(status: number, problem: { detail?: string; title?: string; code?: string; errors?: Record<string, string[]> }) {
    super(problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.')
    this.status = status
    this.code = problem.code
    this.fields = problem.errors ?? {}
  }
}

const REFRESH = '/api/v1/auth/refresh'
let refreshing: Promise<boolean> | null = null

/** Um único refresh por vez, mesmo com várias chamadas recebendo 401 juntas (FND-09c CA 3). */
function refreshSession(): Promise<boolean> {
  refreshing ??= fetch(REFRESH, { method: 'POST', credentials: 'same-origin', headers: { 'X-CSRF': '1' } })
    .then(async (r) => {
      if (!r.ok) return false
      const body = (await r.json()) as { accessToken?: string }
      setAccessToken(body.accessToken ?? null)
      return !!body.accessToken
    })
    .catch(() => false)
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

/** Sem sessão: vai para o login (do tenant ou da plataforma) guardando a rota para voltar depois. */
export function redirectToLogin(login = '/entrar'): void {
  setAccessToken(null)
  const back = window.location.pathname + window.location.search
  if (!window.location.pathname.startsWith(login)) window.location.assign(login + '?volta=' + encodeURIComponent(back))
}

const PLATFORM = '/api/v1/platform/'

async function send(url: string, options: RequestInit): Promise<Response> {
  const headers = new Headers(options.headers)
  const token = getAccessToken()
  if (token) headers.set('Authorization', 'Bearer ' + token)
  if (options.method && options.method !== 'GET') headers.set('X-CSRF', '1')
  return fetch(url, { ...options, headers, credentials: 'same-origin' })
}

/**
 * Mutator único do Orval (CLAUDE.md: nenhum fetch à mão fora daqui). Mesma origem, token em memória, anti-CSRF, refresh
 * silencioso no 401 e ProblemDetails convertido em ApiError.
 */
export async function apiFetch<T>(url: string, options: RequestInit = {}): Promise<T> {
  let response = await send(url, options)
  // Plataforma (FND-13): a sessão é o cookie do super admin, sem refresh; 401 leva ao login da plataforma.
  if (response.status === 401 && url.startsWith(PLATFORM) && !url.startsWith(PLATFORM + 'auth/')) {
    redirectToLogin('/plataforma/entrar')
  } else if (response.status === 401 && !url.startsWith('/api/v1/auth/') && !url.startsWith(PLATFORM)) {
    if (await refreshSession()) {
      response = await send(url, options)
    }

    if (response.status === 401) {
      redirectToLogin()
    }
  }

  trackPermVersion(response.headers.get('X-Perm-Version'))
  const text = response.status === 204 ? '' : await response.text()
  const body: unknown = text ? JSON.parse(text) : undefined
  if (!response.ok) {
    throw new ApiError(response.status, (body ?? {}) as ConstructorParameters<typeof ApiError>[1])
  }

  return body as T
}

export default apiFetch
