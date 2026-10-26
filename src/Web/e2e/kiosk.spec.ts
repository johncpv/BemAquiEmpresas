import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const TOKEN = 'Q2FiZWNhLWRhLVRWLWRhLU1vbnRhZ2VtLTI1Ni1iaXRzLWFiYw'
const VIEWS = ['board', 'queue', 'late']

const row = (over: Record<string, unknown> = {}) => ({
  id: 's1', name: 'TV da Montagem', line: 'Linha 1', views: VIEWS, rotationSeconds: 20, allowedNetworks: [], tokenPrefix: TOKEN.slice(0, 6),
  createdByName: 'Ana Souza', createdAt: new Date().toISOString(), lastSeenAt: null, activeSessions: 0, status: 'AwaitingConnection', needsNewLink: false,
  revokedAt: null, revokedByName: null, version: 1, ...over,
})

/** API simulada das telas (backoffice) e do quiosque (TV), com o estado compartilhado entre as duas páginas. */
function kioskApi() {
  const state = { screens: [] as ReturnType<typeof row>[], consumed: false, revoked: false, created: [] as unknown[] }
  const backoffice = {
    'GET /api/v1/kiosk-screens': (r: Route) => json(r, { screens: state.screens, lines: ['Linha 1', 'Linha 2'], views: VIEWS }),
    'POST /api/v1/kiosk-screens': (r: Route) => {
      state.created.push(r.request().postDataJSON())
      state.screens = [row()]
      return json(r, { screen: state.screens[0], link: `https://app.konebras.com.br/tv#${TOKEN}` }, 201)
    },
    'POST /api/v1/kiosk-screens/s1/revoke': (r: Route) => {
      state.revoked = true
      state.screens = [row({ status: 'Revoked', revokedAt: new Date().toISOString(), revokedByName: 'Ana Souza', version: 3 })]
      return json(r, state.screens[0])
    },
  }
  const tv = {
    'POST /api/v1/kiosk/session': (r: Route) => {
      const body = r.request().postDataJSON() as { token?: string }
      if (state.consumed || body.token !== TOKEN) return json(r, { code: 'kiosk_link_invalid', detail: 'Link inválido ou desligado.' }, 401)
      state.consumed = true
      state.screens = [row({ status: 'Active', activeSessions: 1, lastSeenAt: new Date().toISOString(), version: 2 })]
      return json(r, { screenName: 'TV da Montagem' })
    },
    'GET /api/v1/kiosk/screen': (r: Route) => state.revoked
      ? json(r, { code: 'kiosk_revoked', companyName: 'Konebras', detail: 'Esta tela foi desligada por Konebras.' }, 401)
      : json(r, { name: 'TV da Montagem', line: 'Linha 1', views: VIEWS, rotationSeconds: 20, companyName: 'Konebras', serverNow: new Date().toISOString() }),
    // Visões do painel (PNL-01b): quadro vazio, fila e atrasos sem linhas.
    'GET /api/v1/kiosk/board': (r: Route) => json(r, { line: 'Linha 1', inProgressCount: 0, lateCount: 0, atRiskCount: 0, columns: [], cards: [], serverNow: new Date().toISOString() }),
    'GET /api/v1/kiosk/queue': (r: Route) => json(r, { line: 'Linha 1', rows: [], total: 0 }),
    'GET /api/v1/kiosk/delays': (r: Route) => json(r, { line: 'Linha 1', rows: [], total: 0 }),
  }
  return { state, backoffice, tv }
}

async function tvPage(page: Page, api: ReturnType<typeof kioskApi>) {
  await mockApi(page, api.tv, { session: false })
  await page.route('**/hubs/kiosk/**', (r) => r.fulfill({ status: 503, body: '' })) // sem hub no E2E: "Reconectando…"
}

test.describe('Painel da TV (PNL-01a)', () => {
  test('criar a tela, abrir o link na TV sem o token na URL, segundo uso recusado e revogar', async ({ page, context }) => {
    test.slow() // cinco cargas de página (backoffice, recarga, duas TVs e a recarga da TV) no servidor de desenvolvimento
    const api = kioskApi()
    await mockApi(page, api.backoffice)
    await page.goto('/app/painel-tv')

    await expect(page.getByRole('heading', { name: 'Painel da TV' })).toBeVisible()
    await expect(page.getByText('Nenhuma tela de TV cadastrada.')).toBeVisible()
    await page.getByRole('button', { name: 'Nova tela' }).first().click()
    const panel = page.getByRole('dialog', { name: 'Nova tela' })
    await panel.getByLabel('Nome').fill('TV da Montagem')
    await panel.getByLabel('Linha').selectOption('Linha 1')
    await panel.getByRole('button', { name: 'Criar tela' }).click()

    const dialog = page.getByRole('dialog', { name: 'Link da tela TV da Montagem' })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByText('Abra este link no navegador da TV. Ele vale para uma conexão só e não aparece de novo.')).toBeVisible()
    const link = await dialog.getByLabel('Link da TV').inputValue()
    expect(link).toBe(`https://app.konebras.com.br/tv#${TOKEN}`)
    expect(api.state.created).toEqual([expect.objectContaining({ name: 'TV da Montagem', line: 'Linha 1', rotationSeconds: 20 })])
    await dialog.getByRole('button', { name: 'Fechar' }).first().click()
    await expect(page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Copiar link' })).toBeVisible()
    await expect(page.getByText('Aguardando conexão')).toBeVisible()
    await expect(page.getByText(/Nunca conectada/)).toBeVisible()

    // A TV abre o link: o token sai do fragmento, vira cookie, e a URL final é só /tv.
    const tv = await context.newPage()
    await tvPage(tv, api)
    await tv.goto(`/tv#${TOKEN}`)
    await expect(tv.getByRole('heading', { name: 'Linha 1' })).toBeVisible()
    await expect(tv.getByText('TV da Montagem')).toBeVisible()
    expect(new URL(tv.url()).pathname).toBe('/tv')
    expect(tv.url()).not.toContain(TOKEN)
    await expect(tv.getByRole('button', { name: 'Tela cheia' })).toBeVisible()
    await expect(tv.locator('main.theme-tv')).toBeVisible()
    await expect(tv.getByRole('navigation', { name: 'Navegação principal' })).toHaveCount(0)

    // Segundo uso do mesmo link (outra TV, ou alguém que copiou): recusado.
    const second = await context.newPage()
    await tvPage(second, api)
    await second.goto(`/tv#${TOKEN}`)
    await expect(second.getByText('Link inválido ou desligado.')).toBeVisible()
    expect(second.url()).not.toContain(TOKEN)

    // Revogar no backoffice: a TV desliga.
    await page.reload()
    await page.getByRole('button', { name: 'Revogar', exact: true }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Revogar a tela TV da Montagem?' })
    await expect(confirm.getByText('A TV TV da Montagem desliga em até 1 minuto. Para voltar a usar, crie uma tela nova.')).toBeVisible()
    await confirm.getByRole('button', { name: 'Revogar tela' }).click()
    await expect(page.getByText('Revogada', { exact: true })).toBeVisible()
    await tv.reload() // a TV conferiria em até 30 s; a recarga faz a mesma conferência agora
    await expect(tv.getByText('Esta tela foi desligada por Konebras.')).toBeVisible()
  })

  test('link antigo /tv/{token} mostra o aviso sem chamar a API', async ({ page }) => {
    const calls: string[] = []
    await mockApi(page, {}, { session: false })
    await page.route('**/api/v1/kiosk/**', (r) => { calls.push(r.request().url()); return json(r, {}) })
    await page.goto('/tv/abc123')
    await expect(page.getByText('Este link antigo foi desligado. Peça um novo link a quem cuida do painel.')).toBeVisible()
    expect(page.url()).not.toContain('abc123')
    expect(calls).toEqual([])
  })

  test('sem kiosk.create, a lista aparece sem "Nova tela" nem "Revogar"', async ({ page }) => {
    const api = kioskApi()
    api.state.screens = [row({ status: 'Active', activeSessions: 1, lastSeenAt: new Date(Date.now() - 7 * 60_000).toISOString() })]
    await mockApi(page, {
      ...api.backoffice,
      'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules().map((m) => (m.code === 'kiosk' ? { ...m, actions: ['view'] } : m)) }),
    })
    await page.goto('/app/painel-tv')
    await expect(page.getByText('TV da Montagem')).toBeVisible()
    await expect(page.getByText(/Visto há 7 min/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Nova tela' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Revogar', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Gerar novo link' })).toHaveCount(0)
  })
})
