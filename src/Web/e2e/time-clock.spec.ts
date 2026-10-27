import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

/** RH-01a: "Meu ponto" no tablet (PIN) e no celular, e a consulta do RH sem a localização na rede. */
const jwt = (amr: string[]) => ['e2e', Buffer.from(JSON.stringify({ sub: 'u1', amr })).toString('base64url'), 'sig'].join('.')
const PIN_TOKEN = jwt(['pin'])
const BANNER = 'Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).'

const clock = (over: Record<string, unknown> = {}) => ({
  banner: BANNER, firstName: 'Diego', serverNow: '2026-10-21T11:02:00Z', today: '2026-10-21', schedule: '08:00–17:48, intervalo 1 h', entries: [],
  suggested: 'In', suggestedVerb: 'Registrar entrada', outOfSequence: false, hint: null, geolocation: 'Off', geoConsentNeeded: false,
  geoConsentText: 'Para que serve a localização: registrar onde a marcação foi feita, como indício. Só o RH com permissão vê. Você pode negar.', geoConsentVersion: '2026-10-27',
  registeredDevice: true, ...over,
})

const receipt = (type: string, text: string, message: string, over: Record<string, unknown> = {}) => ({
  id: 'te-' + type, type, at: '2026-10-21T11:02:00Z', day: '2026-10-21', source: 'Pin', location: 'NotAsked', title: 'Comprovante de controle interno', text, message,
  outOfSequence: false, hint: null, sessionEnded: true, ...over,
})

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
}

test.describe('Meu ponto (RH-01a)', () => {
  test('tablet a 400 px: entrada e intervalo pelo PIN, comprovante e a volta à lista de nomes', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    const marks: { body: unknown; key: string | null }[] = []
    let current = clock()
    await mockApi(page, {
      'POST /api/v1/auth/refresh': (r: Route) => json(r, { code: 'auth.session_expired' }, 422),
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: false } }) }),
      'GET /api/v1/auth/pin/users': (r: Route) => json(r, { device: { name: 'Tablet Linha 1', theme: 'System' }, users: [{ userId: 'u1', displayName: 'Diego S.' }] }),
      'POST /api/v1/auth/pin': (r: Route) => json(r, { accessToken: PIN_TOKEN, expiresAt: '2030-01-01T00:00:00Z', sessionExpiresAt: '2030-01-01T00:00:00Z' }),
      'GET /api/v1/shopfloor/queue': (r: Route) => json(r, { operator: { employeeId: 'e1', name: 'Diego Santos', firstName: 'Diego', workCenterId: null, workCenterName: null, line: null, today: '2026-10-21' }, current: null, steps: [], serverNow: '2026-10-21T11:02:00Z' }),
      'GET /api/v1/shopfloor/device': (r: Route) => json(r, { registered: true, deviceId: 'd1', name: 'Tablet Linha 1', theme: 'System', expiresAt: '2027-04-20T12:00:00Z', hasMyPin: true, canCreatePin: false, pinUnavailableReason: null, canRegister: false, registerUnavailableReason: null }),
      'GET /api/v1/me/hr/time-entries/today': (r: Route) => json(r, current),
      'POST /api/v1/me/hr/time-entries': (r: Route) => {
        const body = r.request().postDataJSON() as { type: string }
        marks.push({ body, key: r.request().headers()['idempotency-key'] ?? null })
        return body.type === 'In'
          ? json(r, receipt('In', 'Entrada registrada às 08:02 de 21/10/2026', 'Entrada registrada'), 201)
          : json(r, receipt('BreakStart', 'Saída para o intervalo registrada às 12:00 de 21/10/2026', 'Saída para o intervalo registrada'), 201)
      },
    })

    const enter = async () => {
      await page.goto('/fab/entrar')
      const help = page.getByRole('dialog', { name: 'Ajuda: entrar no app do operador' })
      if (await help.isVisible().catch(() => false)) await help.getByRole('button', { name: 'Entendi' }).click()
      await page.getByRole('button', { name: 'Diego S.' }).click()
      for (const digit of '483920') await page.getByRole('group', { name: 'Teclado numérico' }).getByRole('button', { name: digit, exact: true }).click()
      await page.getByRole('dialog', { name: 'Olá, Diego S.' }).getByRole('button', { name: 'Entrar' }).click()
      await expect(page).toHaveURL(/\/fab$/)
      await page.getByRole('link', { name: /Meu ponto/ }).click()
      await expect(page).toHaveURL(/\/fab\/ponto$/)
    }

    await enter()
    await expect(page.getByRole('note')).toContainText(BANNER)
    await expect(page.getByRole('heading', { name: 'Olá, Diego' })).toBeVisible()
    await expect(page.getByText('Jornada: 08:00–17:48, intervalo 1 h')).toBeVisible()
    await expect(page.getByText('Nenhuma marcação hoje.')).toBeVisible()
    const button = page.getByRole('button', { name: 'Registrar entrada' })
    expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(56)
    await noHorizontalScroll(page)
    await button.click()

    await expect(page.getByRole('region', { name: 'Comprovante de controle interno' })).toContainText('Entrada registrada às 08:02 de 21/10/2026')
    await expect(page.getByRole('status').filter({ hasText: 'Entrada registrada' }).first()).toBeVisible()
    await expect(page.getByText(/Voltando à lista de nomes em \d+ s/)).toBeVisible()
    await noHorizontalScroll(page)
    await page.getByRole('button', { name: 'Pronto' }).click()
    await expect(page).toHaveURL(/\/fab\/entrar$/)
    expect(marks[0].body).toEqual({ type: 'In' })
    expect(marks[0].key).toMatch(/^[0-9a-f-]{36}$/)

    // Volta do almoço: o próximo login sugere a saída para o intervalo.
    current = clock({ entries: [{ id: 'te-In', at: '2026-10-21T11:02:00Z', type: 'In', label: 'Entrada', source: 'Pin', location: 'NotAsked' }], suggested: 'BreakStart', suggestedVerb: 'Registrar saída para o intervalo', serverNow: '2026-10-21T15:00:00Z' })
    await enter()
    await expect(page.getByRole('list').filter({ hasText: 'Entrada' })).toContainText('08:02')
    await page.getByRole('button', { name: 'Registrar saída para o intervalo' }).click()
    await expect(page.getByRole('region', { name: 'Comprovante de controle interno' })).toContainText('Saída para o intervalo registrada às 12:00 de 21/10/2026')
    expect(marks[1].body).toEqual({ type: 'BreakStart' })
    expect(marks[1].key).not.toBe(marks[0].key)
  })

  test('celular a 400 px: aviso da localização antes do primeiro pedido, marcação com a localização e sem conexão', async ({ page, context }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    await context.grantPermissions(['geolocation'])
    await context.setGeolocation({ latitude: -23.55052, longitude: -46.633308, accuracy: 20 })
    const marks: unknown[] = []
    let consented = 0
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: false } }) }),
      'GET /api/v1/me/hr/link': (r: Route) => json(r, { name: 'Ana Souza', situation: 'Active' }),
      'GET /api/v1/me/hr/time-entries/today': (r: Route) => json(r, clock({ firstName: 'Ana', geolocation: 'Ask', geoConsentNeeded: consented === 0, registeredDevice: false })),
      'POST /api/v1/me/hr/time-entries/geo-consent': (r: Route) => { consented++; return json(r, { textVersion: '2026-10-27', acceptedAt: '2026-10-21T11:01:00Z' }) },
      'POST /api/v1/me/hr/time-entries': (r: Route) => { marks.push(r.request().postDataJSON()); return json(r, receipt('In', 'Entrada registrada às 08:02 de 21/10/2026', 'Entrada registrada', { source: 'App', location: 'Captured', sessionEnded: false }), 201) },
      'GET /api/v1/me/hr/time-entries': (r: Route) => json(r, [{ id: 'te-In', at: '2026-10-21T11:02:00Z', day: '2026-10-21', type: 'In', source: 'App', deviceName: null, location: 'Captured', status: 'Valid', latitude: -23.5505, longitude: -46.6333, accuracyM: 20 }]),
    })
    await page.goto('/app/meu-ponto')
    await expect(page.getByRole('heading', { name: 'Meu ponto' })).toBeVisible()
    await expect(page.getByRole('note')).toContainText(BANNER)
    await noHorizontalScroll(page)

    await page.getByRole('button', { name: 'Registrar entrada' }).click()
    const notice = page.getByRole('alertdialog', { name: 'Localização no ponto' })
    await expect(notice).toContainText('Para que serve a localização: registrar onde a marcação foi feita, como indício. Só o RH com permissão vê. Você pode negar.')
    await notice.getByRole('button', { name: 'Entendi e continuar' }).click()
    await expect(page.getByRole('region', { name: 'Comprovante de controle interno' })).toContainText('Entrada registrada às 08:02 de 21/10/2026')
    expect(consented).toBe(1)
    expect(marks[0]).toMatchObject({ type: 'In', latitude: -23.55052, longitude: -46.633308 })

    await page.getByRole('button', { name: 'Ver os últimos 60 dias' }).click()
    await expect(page.getByRole('list', { name: 'Minhas marcações' })).toContainText('-23,5505, -46,6333 (± 20 m)')
    await noHorizontalScroll(page)

    await context.setOffline(true)
    await page.getByRole('button', { name: 'Registrar entrada' }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'Sem conexão. O ponto precisa de rede para usar a hora do servidor. Tente de novo ou avise o RH.' })).toBeVisible()
    expect(marks).toHaveLength(1)
    await context.setOffline(false)
  })

  test('sem vínculo com funcionário ativo, o aviso para falar com o RH', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/me/hr/time-entries/today': (r: Route) => json(r, { status: 403, code: 'employee_not_active', detail: 'Seu usuário não está ligado a um funcionário ativo. Fale com o RH.' }, 403),
    })
    await page.goto('/app/meu-ponto')
    await expect(page.getByText('Seu usuário não está ligado a um funcionário ativo. Fale com o RH.')).toBeVisible()
  })
})

test.describe('RH → Ponto → Marcações (RH-01a)', () => {
  const list = {
    from: '2026-10-15', to: '2026-10-21', truncated: false, items: [
      { id: 'a1', employeeId: 'e1', employeeName: 'Ana Lima', day: '2026-10-21', at: '2026-10-21T11:02:00Z', type: 'In', source: 'Pin', deviceName: 'Tablet Linha 1', location: 'NotAsked', status: 'Valid' },
      { id: 'a2', employeeId: 'e2', employeeName: 'Bia Rocha', day: '2026-10-21', at: '2026-10-21T11:05:00Z', type: 'In', source: 'App', deviceName: null, location: 'Captured', status: 'Valid' },
      { id: 'a3', employeeId: 'e2', employeeName: 'Bia Rocha', day: '2026-10-20', at: '2026-10-20T21:00:00Z', type: 'Out', source: 'App', deviceName: null, location: 'Missing', status: 'Valid' },
    ],
  }

  for (const sensitive of [false, true]) {
    test(sensitive ? 'com hr.view_sensitive, a localização vem só pela rota /sensitive, sob demanda' : 'sem hr.view_sensitive, a localização nunca chega na rede', async ({ page }) => {
      const requests: string[] = []
      page.on('request', (r) => { if (r.url().includes('/api/v1/')) requests.push(r.url()) })
      const actions = ['view', ...(sensitive ? ['view_sensitive'] : [])]
      await mockApi(page, {
        'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: true } }).map((m) => (m.code === 'hr' ? { ...m, actions } : m)) }),
        'GET /api/v1/hr/time-entries': (r: Route) => json(r, list),
        'GET /api/v1/hr/employees': (r: Route) => json(r, { items: [{ id: 'e1', name: 'Ana Lima' }, { id: 'e2', name: 'Bia Rocha' }], page: 1, pageSize: 100, total: 2 }),
        'GET /api/v1/hr/positions/work-centers': (r: Route) => json(r, [{ id: 'wc1', name: 'Montagem' }]),
        'GET /api/v1/hr/employees/e2/time-entries/sensitive': (r: Route) => json(r, [{ id: 'a2', at: '2026-10-21T11:05:00Z', type: 'In', location: 'Captured', latitude: -23.5505, longitude: -46.6333, accuracyM: 12 }]),
      })
      await page.goto('/app/rh/ponto/marcacoes?de=2026-10-15&ate=2026-10-21')
      await expect(page.getByRole('note')).toContainText(BANNER)
      const days = page.getByRole('list', { name: 'Marcações por dia' })
      await expect(days).toContainText('PIN no Tablet Linha 1')
      await expect(days).toContainText('Sem localização')
      expect(requests.some((u) => u.includes('from=2026-10-15') && u.includes('to=2026-10-21'))).toBe(true)
      if (!sensitive) {
        await expect(days.getByText('Dados protegidos')).toBeVisible()
        await expect(page.getByRole('button', { name: /Ver localização/ })).toHaveCount(0)
        expect(requests.filter((u) => u.includes('/sensitive'))).toEqual([])
      } else {
        expect(requests.filter((u) => u.includes('/sensitive'))).toEqual([])
        await page.getByRole('button', { name: /Ver localização/ }).click()
        await expect(days).toContainText('-23,5505, -46,6333 (± 12 m)')
        expect(requests.filter((u) => u.includes('/sensitive'))).toHaveLength(1)
      }
    })
  }
})
