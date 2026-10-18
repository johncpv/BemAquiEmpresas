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
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules() }),
    'GET /api/v1/me/preferences': (r) => json(r, { theme: 'system' }),
    'PUT /api/v1/me/preferences': (r) => json(r, { theme: 'system' }),
    'GET /api/v1/notifications/': (r) => json(r, { page: { items: [], page: 1, pageSize: 10, total: 0 }, unread: 0 }),
    'POST /api/v1/auth/logout': (r) => r.fulfill({ status: 204 }),
    'GET /api/v1/identity/groups': (r) => json(r, []),
    'GET /api/v1/ajuda': (r) => json(r, []),
    'GET /api/v1/ajuda/vistas': (r) => json(r, []),
    'GET /api/v1/identity/users': (r) => json(r, []),
    'GET /api/v1/settings/security': (r) => json(r, { requireTwoFactor: false }),
  }
  const handlers = { ...defaults, ...overrides }

  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const key = `${route.request().method()} ${url.pathname}`
    const bare = key.endsWith('/') ? key.slice(0, -1) : key
    const handler = handlers[key] ?? handlers[bare] ?? handlers[bare + '/']
    if (handler) return handler(route)
    return json(route, {})
  })
}

export { json }

const CATALOG: [string, string, string][] = [
  ['dashboard', 'Painel do proprietário', 'Visão geral'], ['quotes', 'Orçamentos', 'Comercial'], ['projects', 'Projetos', 'Comercial'], ['parties', 'Cadastros', 'Comercial'],
  ['engineering', 'Engenharia', 'Fábrica'], ['production', 'Produção', 'Fábrica'], ['inventory', 'Estoque', 'Fábrica'], ['purchasing', 'Compras', 'Fábrica'], ['assets', 'Patrimônio', 'Fábrica'],
  ['finance', 'Caixa e pagamentos', 'Financeiro'], ['fiscal', 'Fiscal', 'Financeiro'], ['hr', 'RH', 'Pessoas'], ['kiosk', 'Painel da TV', 'Telas dedicadas'], ['shopfloor', 'App do operador', 'Telas dedicadas'],
  ['identity', 'Usuários e acessos', 'Sistema'], ['settings', 'Configurações', 'Sistema'],
]

/** Formato de /me/permissions; por padrão tudo liberado e com acesso. */
export function allModules(change: Record<string, { released?: boolean; enabled?: boolean }> = {}) {
  return CATALOG.map(([code, name, area], i) => ({ code, name, area, order: i * 10, description: 'O que ' + name + ' faz.', released: true, enabled: true, actions: ['view', 'create', 'edit', 'approve', 'delete'], ...change[code] }))
}
