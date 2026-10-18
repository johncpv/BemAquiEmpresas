import type { Page, Route } from '@playwright/test'

type Handler = (route: Route) => Promise<void> | void
const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

/**
 * API simulada para o E2E do front: sessão válida, usuário, tenant e listas vazias. Cada teste pode sobrescrever
 * rotas (ex.: login com 2FA). O backend real é coberto pelos testes de integração (.NET).
 */
export async function mockApi(page: Page, overrides: Record<string, Handler> = {}, options: { session?: boolean } = {}) {
  const session = options.session ?? true
  const defaults: Record<string, Handler> = {
    'POST /api/v1/auth/refresh': (r) => (session ? json(r, { accessToken: 'tok-e2e', expiresAt: '2030-01-01T00:00:00Z' }) : json(r, { code: 'auth.session_expired' }, 422)),
    'GET /api/v1/public/tenant-context': (r) => json(r, { name: 'Konebras', logoUrl: null, status: 'Active' }),
    'GET /api/v1/me': (r) => json(r, { userId: 'u1', tenantId: 't1', name: 'Ana Souza', email: 'ana@konebras.com.br' }),
    'GET /api/v1/me/preferences': (r) => json(r, { theme: 'system' }),
    'PUT /api/v1/me/preferences': (r) => json(r, { theme: 'system' }),
    'GET /api/v1/notifications/': (r) => json(r, { page: { items: [], page: 1, pageSize: 10, total: 0 }, unread: 0 }),
    'POST /api/v1/auth/logout': (r) => r.fulfill({ status: 204 }),
  }
  const handlers = { ...defaults, ...overrides }

  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const key = `${route.request().method()} ${url.pathname}`
    const handler = handlers[key] ?? handlers[key.replace(/\/$/, '')]
    if (handler) return handler(route)
    return json(route, {})
  })
}

export { json }
