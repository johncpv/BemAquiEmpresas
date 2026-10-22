import { expect, type Locator, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

/** Token com o amr no payload (o front só lê o amr para decidir a tela; o servidor confere tudo). */
const jwt = (amr: string[]) => ['e2e', Buffer.from(JSON.stringify({ sub: 'u1', amr })).toString('base64url'), 'sig'].join('.')
const PIN_TOKEN = jwt(['pin'])
const FULL_TOKEN = jwt(['pwd'])

const operator = { employeeId: 'e1', name: 'Diego Santos', firstName: 'Diego', workCenterId: 'wc1', workCenterName: 'Montagem', line: 'Linha 1', today: '2026-10-16' }
const queue = { operator, current: null, steps: [], serverNow: new Date().toISOString() }
const users = { device: { name: 'Tablet Linha 1', theme: 'System' }, users: [{ userId: 'u1', displayName: 'Diego S.' }, { userId: 'u2', displayName: 'Ana P.' }] }
const deviceContext = (over: Record<string, unknown> = {}) => ({
  registered: true, deviceId: 'd1', name: 'Tablet Linha 1', theme: 'System', expiresAt: '2027-04-20T12:00:00Z', hasMyPin: false, canCreatePin: false,
  pinUnavailableReason: 'Para criar o PIN, entre com e-mail e senha.', canRegister: false, registerUnavailableReason: 'Este aparelho já está registrado.', ...over,
})

async function fabApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}, session: string | null = null) {
  const calls: Record<string, unknown[]> = {}
  const record = (key: string, r: Route) => { (calls[key] ??= []).push(r.request().postDataJSON?.() ?? null) }
  await mockApi(page, {
    'POST /api/v1/auth/refresh': (r: Route) => (session ? json(r, { accessToken: session, expiresAt: '2030-01-01T00:00:00Z' }) : json(r, { code: 'auth.session_expired', detail: 'Sua sessão expirou. Entre de novo.' }, 422)),
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules() }),
    'GET /api/v1/auth/pin/users': (r: Route) => { record('users', r); return json(r, users) },
    'POST /api/v1/auth/pin': (r: Route) => { record('pin', r); return json(r, { accessToken: PIN_TOKEN, expiresAt: '2030-01-01T00:00:00Z', sessionExpiresAt: '2030-01-01T00:00:00Z' }) },
    'POST /api/v1/auth/logout': (r: Route) => { record('logout', r); return r.fulfill({ status: 204 }) },
    'GET /api/v1/shopfloor/queue': (r: Route) => json(r, queue),
    'GET /api/v1/shopfloor/device': (r: Route) => json(r, deviceContext()),
    'PUT /api/v1/shopfloor/device/theme': (r: Route) => { record('theme', r); return json(r, deviceContext({ theme: (r.request().postDataJSON() as { theme: string }).theme })) },
    ...overrides,
  })
  return calls
}

/** Todo alvo de toque visível tem 56 × 56 px ou mais (padrão 1). */
async function expectTargets(scope: Locator) {
  const boxes = await scope.locator('button:visible, a:visible').evaluateAll((els) =>
    els.map((e) => { const r = e.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), text: (e.textContent ?? e.getAttribute('aria-label') ?? '').trim().slice(0, 40) } }))
  expect(boxes.length).toBeGreaterThan(0)
  for (const b of boxes) {
    expect(b.h, `altura de "${b.text}"`).toBeGreaterThanOrEqual(56)
    expect(b.w, `largura de "${b.text}"`).toBeGreaterThanOrEqual(56)
  }
}

async function enterPin(page: Page, pin: string) {
  const pad = page.getByRole('group', { name: 'Teclado numérico' })
  for (const digit of pin) await pad.getByRole('button', { name: digit, exact: true }).click()
}

test.describe('App do operador: aparelho registrado e PIN (PRD-03b)', () => {
  test('entrada do tablet: ajuda na primeira visita, nomes do aparelho, PIN no teclado grande e a fila', async ({ page }) => {
    const calls = await fabApi(page)
    await page.goto('/fab/entrar')

    const help = page.getByRole('dialog', { name: 'Ajuda: entrar no app do operador' })
    await expect(help).toBeVisible()
    await expect(help.getByRole('heading', { name: 'Como usar' })).toBeVisible()
    await help.getByRole('button', { name: 'Entendi' }).click()
    await expect(page.getByText('Tablet Linha 1')).toBeVisible()
    await expectTargets(page.locator('main'))

    await page.getByRole('button', { name: 'Diego S.' }).click()
    const sheet = page.getByRole('dialog', { name: 'Olá, Diego S.' })
    await expect(sheet.getByRole('button', { name: 'Entrar' })).toBeDisabled()
    await enterPin(page, '483920')
    await expect(sheet.getByLabel('PIN: 6 de 6 números')).toHaveText('••••••')
    await expectTargets(sheet)
    await sheet.getByRole('button', { name: 'Entrar' }).click()

    await expect(page).toHaveURL(/\/fab$/)
    await expect(page.getByText('Olá, Diego')).toBeVisible()
    expect(calls.pin).toEqual([{ userId: 'u1', pin: '483920' }])

    await page.goto('/fab/entrar')
    await expect(page.getByRole('button', { name: 'Diego S.' })).toBeVisible()
    await expect(page.getByRole('dialog', { name: 'Ajuda: entrar no app do operador' })).toHaveCount(0)
  })

  test('PIN errado e bloqueado mostram a mensagem do servidor na folha', async ({ page }) => {
    await fabApi(page, {
      'POST /api/v1/auth/pin': (r: Route) => json(r, { code: 'auth.pin_locked', detail: 'PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere.' }, 422),
    })
    await page.goto('/fab/entrar')
    await page.getByRole('button', { name: 'Entendi' }).click()
    await page.getByRole('button', { name: 'Ana P.' }).click()
    await enterPin(page, '705218')
    await page.getByRole('dialog', { name: 'Olá, Ana P.' }).getByRole('button', { name: 'Entrar' }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere.' })).toBeVisible()
  })

  test('aparelho não registrado: a lista não existe e o login completo vem marcado como do tablet', async ({ page }) => {
    await fabApi(page, { 'GET /api/v1/auth/pin/users': (r: Route) => r.fulfill({ status: 404 }) })
    await page.goto('/fab/entrar')
    await page.getByRole('button', { name: 'Entendi' }).click()

    await expect(page.getByText('Este aparelho não está registrado. Entre com e-mail e senha.')).toBeVisible()
    await page.getByRole('link', { name: 'Entrar com e-mail e senha' }).click()
    await expect(page.getByRole('heading', { name: 'Entrar no tablet da fábrica' })).toBeVisible()
    await expect(page.getByText('Esta entrada não fica salva no tablet')).toBeVisible()
  })

  test('15 min sem toque avisam "Você vai sair em 60 s." e, sem toque, saem para a lista de nomes', async ({ page }) => {
    await page.clock.install()
    const calls = await fabApi(page, {}, PIN_TOKEN)
    await page.goto('/fab')
    await expect(page.getByText('Olá, Diego')).toBeVisible()

    await page.clock.fastForward('15:01')
    const warning = page.getByRole('alertdialog', { name: 'Você vai sair em 60 s. Tocar para continuar.' })
    await expect(warning).toBeVisible()
    await warning.getByRole('button', { name: 'Continuar' }).click()
    await expect(warning).toHaveCount(0)

    await page.clock.fastForward('16:05')
    await expect(page).toHaveURL(/\/fab\/entrar$/)
    expect(calls.logout?.length ?? 0).toBeGreaterThan(0)
  })

  test('tema do /fab fica no aparelho e o "Apagar meu PIN" encerra a sessão por PIN', async ({ page }) => {
    const calls = await fabApi(page, {
      'GET /api/v1/shopfloor/device': (r: Route) => json(r, deviceContext({ hasMyPin: true })),
      'PUT /api/v1/shopfloor/device/theme': (r: Route) => { (calls.theme ??= []).push(r.request().postDataJSON()); return json(r, deviceContext({ hasMyPin: true, theme: (r.request().postDataJSON() as { theme: string }).theme })) },
      'DELETE /api/v1/shopfloor/device/pin': (r: Route) => r.fulfill({ status: 200 }),
    }, PIN_TOKEN)
    await page.goto('/fab')
    await page.getByRole('button', { name: 'Este aparelho: tema, PIN e sair' }).click()
    const sheet = page.getByRole('dialog', { name: 'Este aparelho: Tablet Linha 1' })
    await sheet.getByRole('button', { name: 'Escuro' }).click()
    await expect.poll(() => page.evaluate(() => document.documentElement.getAttribute('data-theme'))).toBe('dark')
    expect(calls.theme).toEqual([{ theme: 'Dark' }])

    await sheet.getByRole('button', { name: 'Apagar meu PIN deste aparelho' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Apagar meu PIN deste aparelho?' })
    await confirm.getByRole('button', { name: 'Apagar meu PIN' }).click()
    await expect(page).toHaveURL(/\/fab\/entrar$/)
  })

  test('login completo num aparelho registrado oferece "Criar PIN" e o PIN vai duas vezes no teclado', async ({ page }) => {
    let created: unknown = null
    await fabApi(page, {
      'GET /api/v1/shopfloor/device': (r: Route) => json(r, deviceContext({ canCreatePin: true, pinUnavailableReason: null })),
      'POST /api/v1/shopfloor/device/pin': (r: Route) => { created = r.request().postDataJSON(); return json(r, deviceContext({ hasMyPin: true })) },
    }, FULL_TOKEN)
    await page.goto('/fab')
    await page.getByRole('region', { name: 'Criar PIN' }).getByRole('button', { name: 'Criar PIN' }).click()
    const sheet = page.getByRole('dialog', { name: 'Criar PIN para este aparelho' })
    await enterPin(page, '483920')
    await sheet.getByRole('button', { name: 'Continuar' }).click()
    const again = page.getByRole('dialog', { name: 'Digite o PIN de novo' })
    await enterPin(page, '483920')
    await again.getByRole('button', { name: 'Criar PIN' }).click()
    await expect(page.getByRole('dialog', { name: 'PIN criado' })).toBeVisible()
    expect(created).toEqual({ pin: '483920', confirmation: '483920' })
    await page.getByRole('button', { name: 'Sair e entrar pelo PIN' }).click()
    await expect(page).toHaveURL(/\/fab\/entrar$/)
  })

  test('tablet sem registro: quem pode registra o aparelho pelo nome', async ({ page }) => {
    let body: unknown = null
    await fabApi(page, {
      'GET /api/v1/shopfloor/device': (r: Route) => json(r, { ...deviceContext(), registered: false, deviceId: null, name: null, expiresAt: null, canRegister: true, registerUnavailableReason: null }),
      'POST /api/v1/shopfloor/device': (r: Route) => { body = r.request().postDataJSON(); return json(r, deviceContext({ canCreatePin: true, pinUnavailableReason: null }), 201) },
    }, FULL_TOKEN)
    await page.goto('/fab')
    await page.getByRole('button', { name: 'Este aparelho: tema, PIN e sair' }).click()
    await page.getByLabel('Nome do aparelho').fill('Tablet Linha 2')
    await page.getByRole('button', { name: 'Registrar este aparelho' }).click()
    await expect(page.getByText('Aparelho registrado')).toBeVisible()
    expect(body).toEqual({ name: 'Tablet Linha 2' })
    await expect(page.getByRole('button', { name: 'Criar PIN para este aparelho' })).toBeVisible()
  })

  test('sem rede, o /fab diz "Sem conexão. Esta ação precisa de rede." e "Tentar de novo" volta quando a rede volta', async ({ page, context }) => {
    await fabApi(page, {}, PIN_TOKEN)
    await page.goto('/fab')
    await expect(page.getByText('Olá, Diego')).toBeVisible()

    await context.setOffline(true)
    const offline = page.getByRole('alertdialog', { name: 'Sem conexão. Esta ação precisa de rede.' })
    await expect(offline).toBeVisible()
    await expect(offline.getByRole('button', { name: 'Tentar de novo' })).toBeVisible()
    await expectTargets(offline)

    await context.setOffline(false)
    await expect(offline).toHaveCount(0)
    await expect(page.getByText('Olá, Diego')).toBeVisible()
  })

  test('o Cache Storage nunca guarda /api/* depois de usar o /fab', async ({ page }) => {
    await fabApi(page, {}, PIN_TOKEN)
    await page.goto('/fab')
    await expect(page.getByText('Olá, Diego')).toBeVisible()
    const cached = await page.evaluate(async () => {
      const urls: string[] = []
      for (const name of await caches.keys()) {
        for (const request of await (await caches.open(name)).keys()) urls.push(request.url)
      }
      return urls
    })
    expect(cached.filter((u) => new URL(u).pathname.startsWith('/api/'))).toEqual([])
  })
})

test.describe('Configurações → Segurança → Aparelhos da fábrica (PRD-03b)', () => {
  const row = (over: Record<string, unknown> = {}) => ({
    id: 'd1', name: 'Tablet Linha 1', createdByName: 'Rita Prado', createdAt: '2026-09-01T12:00:00Z', lastSeenAt: '2026-09-25T12:00:00Z', expiresAt: '2027-02-28T12:00:00Z',
    revokedAt: null, revokedByName: null, revokedReason: null, status: 'Active', daysUnused: 26, pins: 3, ...over,
  })

  test('lista com "Sem uso há {n} dias" e "Revogar aparelho" com o mesmo verbo', async ({ page }) => {
    let revoked = false
    await mockApi(page, {
      'GET /api/v1/settings/security/shopfloor-devices': (r: Route) => json(r, { devices: [revoked ? row({ status: 'Revoked', revokedAt: '2026-10-22T12:00:00Z', revokedByName: 'Ana Souza', revokedReason: 'manual' }) : row()], activeCount: revoked ? 0 : 1, maxActive: 10 }),
      'POST /api/v1/settings/security/shopfloor-devices/d1/revoke': (r: Route) => { revoked = true; return json(r, { devices: [row({ status: 'Revoked', revokedAt: '2026-10-22T12:00:00Z', revokedByName: 'Ana Souza', revokedReason: 'manual' })], activeCount: 0, maxActive: 10 }) },
    })
    await page.goto('/app/configuracoes/seguranca')
    const section = page.getByRole('region', { name: /Aparelhos da fábrica/ })
    await expect(section.getByText('Tablet Linha 1')).toBeVisible()
    await expect(section.getByText('Sem uso há 26 dias')).toBeVisible()
    await expect(section.getByText('1 de 10 ativos')).toBeVisible()
    await section.getByRole('button', { name: 'Revogar aparelho' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Revogar aparelho' }).click()
    await expect(page.getByText('Aparelho revogado')).toBeVisible()
    await expect(section.getByText('Revogado por Ana Souza', { exact: false })).toBeVisible()
  })

  test('sem aparelhos, o vazio explica como registrar', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/settings/security/shopfloor-devices': (r: Route) => json(r, { devices: [], activeCount: 0, maxActive: 10 }) })
    await page.goto('/app/configuracoes/seguranca')
    await expect(page.getByText('Nenhum aparelho da fábrica registrado.')).toBeVisible()
  })
})