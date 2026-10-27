import { getAccessToken, setAccessToken, trackPermVersion } from './session'
import { requestStepUp, STEP_UP_URL, TWO_FACTOR_REQUIRED } from './step-up'

/** Erro da API no formato ProblemDetails (RFC 9457) com code e errors por campo (FND-07). */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fields: Record<string, string[]>
  /** Extensões do ProblemDetails além de code e errors (ex.: existingId no 409 party_exists). */
  readonly data: Record<string, unknown>

  constructor(status: number, problem: { detail?: string; title?: string; code?: string; errors?: Record<string, string[]>; [key: string]: unknown }) {
    super(problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.')
    this.status = status
    this.code = problem.code
    this.fields = problem.errors ?? {}
    this.data = problem
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

/** Mensagem do kit quando a rede cai (PRD-03b CA 3): nada fica guardado para enviar depois (ADR 0026 §6). */
export const OFFLINE_MESSAGE = 'Sem conexão. Esta ação precisa de rede.'
export const OFFLINE_CODE = 'offline'

/** No app do operador, sem sessão volta à entrada do tablet (/fab/entrar: lista de nomes e PIN), não ao login do backoffice. */
export const FAB_ENTRY = '/fab/entrar'

/** Sem sessão: vai para o login (do tenant ou da plataforma) guardando a rota para voltar depois. */
export function redirectToLogin(login = '/entrar'): void {
  setAccessToken(null)
  if (login === '/entrar' && window.location.pathname.startsWith('/fab')) {
    if (!window.location.pathname.startsWith(FAB_ENTRY)) window.location.assign(FAB_ENTRY)
    return
  }

  const back = window.location.pathname + window.location.search
  if (!window.location.pathname.startsWith(login)) window.location.assign(login + '?volta=' + encodeURIComponent(back))
}

const PLATFORM = '/api/v1/platform/'

/** API do quiosque (TV): sem refresh nem login no 401; a TV mostra o estado da tela (ADR 0030). */
export const isKioskUrl = (url: string) => url.startsWith('/api/v1/kiosk/')

async function send(url: string, options: RequestInit): Promise<Response> {
  const headers = new Headers(options.headers)
  const token = getAccessToken()
  // Painel da TV (PNL-01a): a sessão é o cookie __Host-bq_kiosk; o token de um usuário nunca vai para a API do quiosque.
  if (token && !isKioskUrl(url)) headers.set('Authorization', 'Bearer ' + token)
  // As rotas do PIN (GET /auth/pin/users) também exigem o X-CSRF: o navegador não manda Origin num GET da mesma origem.
  if ((options.method && options.method !== 'GET') || url.startsWith('/api/v1/auth/')) headers.set('X-CSRF', '1')
  try {
    return await fetch(url, { ...options, headers, credentials: 'same-origin' })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiError(0, { code: OFFLINE_CODE, detail: OFFLINE_MESSAGE })
  }
}

/** Uma ida à API com o refresh silencioso no 401; devolve a resposta e o corpo já lido. */
async function exchange(url: string, options: RequestInit): Promise<{ response: Response; body: unknown }> {
  let response = await send(url, options)
  // Plataforma (FND-13): a sessão é o cookie do super admin, sem refresh; 401 leva ao login da plataforma.
  if (response.status === 401 && url.startsWith(PLATFORM) && !url.startsWith(PLATFORM + 'auth/')) {
    redirectToLogin('/plataforma/entrar')
  } else if (response.status === 401 && !url.startsWith('/api/v1/auth/') && !url.startsWith(PLATFORM) && !isKioskUrl(url)) {
    if (await refreshSession()) {
      response = await send(url, options)
    }

    if (response.status === 401) {
      redirectToLogin()
    }
  }

  trackPermVersion(response.headers.get('X-Perm-Version'))
  const text = response.status === 204 ? '' : await response.text()
  return { response, body: text ? JSON.parse(text) : undefined }
}

/**
 * Mutator único do Orval (CLAUDE.md: nenhum fetch à mão fora daqui). Mesma origem, token em memória, anti-CSRF, refresh
 * silencioso no 401 e ProblemDetails convertido em ApiError. No 403 two_factor_required (BNK-01a CA 1), abre o passo
 * "Confirme o código" e, confirmado, repete a mesma requisição uma vez (a recusa não gravou nada).
 */
export async function apiFetch<T>(url: string, options: RequestInit = {}): Promise<T> {
  let result = await exchange(url, options)
  const problem = (result.body ?? {}) as { code?: string; enrolled?: boolean }
  if (result.response.status === 403 && problem.code === TWO_FACTOR_REQUIRED && url !== STEP_UP_URL && (await requestStepUp(problem.enrolled === true))) {
    result = await exchange(url, options)
  }

  const { response, body } = result

  if (!response.ok) {
    throw new ApiError(response.status, (body ?? {}) as ConstructorParameters<typeof ApiError>[1])
  }

  return body as T
}

export default apiFetch

/** Baixa um arquivo autenticado (ex.: comprovante da OP) e oferece para salvar com o nome dado. */
export async function apiDownload(url: string, fileName: string): Promise<void> {
  let response = await send(url, {})
  if (response.status === 401 && (await refreshSession())) response = await send(url, {})
  if (!response.ok) {
    const text = await response.text()
    throw new ApiError(response.status, (text ? JSON.parse(text) : {}) as ConstructorParameters<typeof ApiError>[1])
  }

  const href = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = href
  link.download = fileName
  link.click()
  URL.revokeObjectURL(href)
}

/** Arquivo autenticado como endereço local (blob:) para abrir na tela, ex.: desenho da OS no app do operador (PRD-03a CA 6). */
export async function apiBlobUrl(url: string): Promise<string> {
  let response = await send(url, {})
  if (response.status === 401 && (await refreshSession())) response = await send(url, {})
  if (!response.ok) {
    const text = await response.text()
    throw new ApiError(response.status, (text ? JSON.parse(text) : {}) as ConstructorParameters<typeof ApiError>[1])
  }

  return URL.createObjectURL(await response.blob())
}