import { expect, type Page, type Route, test, type WebSocketRoute } from '@playwright/test'
import { json, mockApi } from './mock-api'

/**
 * PNL-01b: as visões do Painel da TV (quadro por etapa, fila e previsões, atrasos e riscos), a leitura a distância a 1920 × 1080 e
 * 1366 × 768, a recarga do cartão pelo tempo real em menos de 2 s, a troca de configuração sem recarregar e a prévia no backoffice.
 * O hub do quiosque é simulado (negociação e WebSocket do SignalR); a API, pelo mock.
 */

const STEPS = [['P', 'Projeto'], ['C', 'Corte'], ['S', 'Solda'], ['M', 'Montagem'], ['Q', 'Qualidade']] as const
const RS = '\u001e'

const card = (i: number, code: string, over: Record<string, unknown> = {}) => ({
  id: `o-${code}-${i}`, number: `OS-2026-${code}${i}`, projectNumber: 'PRJ-2026-0007', projectName: 'Residencial Aurora', columnKey: code, stepCode: code,
  stepName: code, operator: i === 0 ? 'João S. +1' : 'Ana L.', pausedReason: null, stepStartedAt: new Date(Date.now() - 95 * 60_000).toISOString(),
  appliedHours: 1.5, standardHours: 3, plannedEnd: '2026-10-30', risk: i === 0 && code === 'C' ? 'Late' : 'None', delayDays: i === 0 && code === 'C' ? 3 : null, ...over,
})

function boardOf(cards: ReturnType<typeof card>[]) {
  return {
    line: 'Linha 1', inProgressCount: cards.length, lateCount: cards.filter((c) => c.risk === 'Late').length, atRiskCount: 0,
    columns: STEPS.map(([code, name]) => ({ key: code, code, name, count: cards.filter((c) => c.columnKey === code).length })).filter((c) => c.count > 0),
    cards, serverNow: new Date().toISOString(),
  }
}

/** API da TV: tela, quadro (5 etapas × 4 OS), fila, atrasos e o cartão de uma OS, com estado para o tempo real. */
function tvApi(views = ['board', 'queue', 'late'], rotationSeconds = 10) {
  const state = {
    views, rotationSeconds,
    cards: STEPS.flatMap(([code]) => [0, 1, 2, 3].map((i) => card(i, code))),
    delays: [{ id: 'o-C-0', number: 'OS-2026-C0', projectNumber: 'PRJ-2026-0007', projectName: 'Residencial Aurora', stepCode: 'C', stepName: 'Corte', operator: 'João S. +1', risk: 'Late', days: 3, definingStepName: 'Qualidade', definingStepEnd: '2026-11-04' }],
    calls: [] as string[],
  }
  const screen = () => ({ name: 'TV da Montagem', line: 'Linha 1', views: state.views, rotationSeconds: state.rotationSeconds, companyName: 'Konebras', serverNow: new Date().toISOString() })
  const queue = {
    line: 'Linha 1', total: 2,
    rows: [
      { position: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Residencial Aurora', firstStart: '2026-10-27', forecastDate: '2026-11-20', promisedDate: '2026-11-30', slackDays: 10, risk: 'None' },
      { position: 3, projectId: 'p2', projectNumber: 'PRJ-2026-0009', projectName: 'Torre Norte', firstStart: null, forecastDate: null, promisedDate: '2026-12-10', slackDays: null, risk: 'None' },
    ],
  }
  const handlers = (prefix: string) => ({
    [`GET ${prefix}/board`]: (r: Route) => { state.calls.push('board'); return json(r, boardOf(state.cards)) },
    [`GET ${prefix}/queue`]: (r: Route) => json(r, queue),
    [`GET ${prefix}/delays`]: (r: Route) => json(r, { line: 'Linha 1', rows: state.delays, total: state.delays.length }),
    [`GET ${prefix}/board/orders/*`]: (r: Route) => {
      const id = new URL(r.request().url()).pathname.split('/').pop()!
      state.calls.push('card:' + id)
      const found = state.cards.find((c) => c.id === id)
      return found ? json(r, found) : json(r, { code: 'not_found', detail: 'Não encontrado neste painel.' }, 404)
    },
  })
  return {
    state,
    tv: {
      'POST /api/v1/kiosk/session': (r: Route) => json(r, { screenName: 'TV da Montagem' }),
      'GET /api/v1/kiosk/screen': (r: Route) => json(r, screen()),
      ...handlers('/api/v1/kiosk'),
    },
    preview: {
      'GET /api/v1/kiosk-screens/s1/preview': (r: Route) => json(r, screen()),
      ...handlers('/api/v1/kiosk-screens/s1/preview'),
    },
  }
}

/** Hub do SignalR simulado: negociação, handshake e o envio das mensagens do servidor. */
async function fakeHub(page: Page, hub: string) {
  const sockets: WebSocketRoute[] = []
  await page.route(`**${hub}/negotiate**`, (r) => r.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ negotiateVersion: 1, connectionId: 'c1', connectionToken: 't1', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] }),
  }))
  await page.routeWebSocket(new RegExp(hub.replaceAll('/', '\\/')), (ws) => {
    sockets.push(ws)
    ws.onMessage((message) => {
      if (String(message).includes('"protocol"')) ws.send('{}' + RS)
    })
  })
  return {
    send: (target: string, ...args: unknown[]) => { for (const ws of sockets) ws.send(JSON.stringify({ type: 1, target, arguments: args }) + RS) },
  }
}

async function openTv(page: Page, api: ReturnType<typeof tvApi>) {
  await mockApi(page, api.tv, { session: false })
  const hub = await fakeHub(page, '/hubs/kiosk')
  await page.goto('/tv#Q2FiZWNhLWRhLVRWLWRhLU1vbnRhZ2VtLTI1Ni1iaXRzLWFiYw')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Linha 1 — 20 OS em andamento, 1 atrasada')
  return hub
}

/** Tamanho calculado da fonte, em px. */
const fontPx = (page: Page, selector: string) => page.locator(selector).first().evaluate((el) => Number.parseFloat(getComputedStyle(el).fontSize))

test.describe('Painel da TV: visões (PNL-01b)', () => {
  test.skip(({ isMobile }) => isMobile, 'a TV não é pensada para celular')

  test('TV 1920 × 1080: 5 colunas com 3 cartões de 4 linhas, "+1 OS", medidas mínimas e sem cliente', async ({ page }, info) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    const api = tvApi(['board'])
    await openTv(page, api)

    const columns = page.locator('[data-tv-column]')
    await expect(columns).toHaveCount(5)
    for (const [code, name] of STEPS) {
      const column = page.locator(`[data-tv-column="${code}"]`)
      await expect(column.getByRole('heading', { name })).toBeVisible()
      await expect(column.locator('[data-tv-card]')).toHaveCount(3)
      await expect(column.getByText('+1 OS')).toBeVisible()
    }

    const late = page.locator('[data-tv-column="C"] [data-tv-card]').first()
    await expect(late).toHaveAttribute('data-risk', 'Late')
    await expect(late.locator('[data-line]')).toHaveCount(4)
    await expect(late.locator('[data-line="2"]')).toHaveText('João S. +1')
    await expect(late.locator('[data-line="3"]')).toContainText('1:35')
    await expect(late.locator('[data-line="4"]')).toHaveText('ATRASADA')
    await expect(page.getByText('Ao vivo')).toBeVisible()
    await expect(page.locator('body')).not.toContainText('Elevador')

    // Leitura a distância (ADR 0030): títulos ≥ 32 px, linhas ≥ 28 px, contagens e relógio ≥ 48 px; tudo dentro da tela.
    expect(await fontPx(page, '[data-tv-column] h2')).toBeGreaterThanOrEqual(32)
    expect(await fontPx(page, '[data-tv-card]')).toBeGreaterThanOrEqual(28)
    expect(await fontPx(page, '[data-tv-column] header > span')).toBeGreaterThanOrEqual(48)
    expect(await fontPx(page, 'time')).toBeGreaterThanOrEqual(48)
    const boxes = await page.locator('[data-tv-card]').evaluateAll((els) => els.map((e) => e.getBoundingClientRect()).map((r) => ({ right: r.right, bottom: r.bottom })))
    expect(boxes.every((b) => b.right <= 1920 && b.bottom <= 1080), 'os 15 cartões cabem na tela sem rolar').toBe(true)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1920)
    await info.attach('tv-1920x1080', { body: await page.screenshot(), contentType: 'image/png' })
  })

  test('TV 1366 × 768: 3 colunas por vez, rolando a cada rotação, com as mesmas medidas mínimas', async ({ page }, info) => {
    await page.setViewportSize({ width: 1366, height: 768 })
    await page.clock.install()
    const api = tvApi(['board'], 10)
    await openTv(page, api)

    await expect(page.locator('[data-tv-column]')).toHaveCount(3)
    await expect(page.getByText('Colunas 1/2')).toBeVisible()
    expect(await fontPx(page, '[data-tv-column] h2')).toBeGreaterThanOrEqual(32)
    expect(await fontPx(page, '[data-tv-card]')).toBeGreaterThanOrEqual(28)
    expect(await fontPx(page, 'time')).toBeGreaterThanOrEqual(48)
    const bottom = await page.locator('[data-tv-card]').evaluateAll((els) => Math.max(...els.map((e) => e.getBoundingClientRect().bottom)))
    expect(bottom).toBeLessThanOrEqual(768)
    await info.attach('tv-1366x768', { body: await page.screenshot(), contentType: 'image/png' })

    await page.clock.runFor(10_000)
    await expect(page.getByText('Colunas 2/2')).toBeVisible()
    await expect(page.locator('[data-tv-column]')).toHaveCount(2)
    await expect(page.locator('[data-tv-column="M"]')).toBeVisible()
  })

  test('rotação entre as visões com o indicador "1/3"; Atrasos e riscos vazia sai da rotação', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    await page.clock.install()
    const api = tvApi(['board', 'queue', 'late'], 10)
    await openTv(page, api)

    await expect(page.getByLabel('Visão 1 de 3: Quadro por etapa')).toBeVisible()
    await page.clock.runFor(10_000)
    await expect(page.getByLabel('Visão 2 de 3: Fila e previsões')).toBeVisible()
    const rows = page.locator('[data-tv-queue-row]')
    await expect(rows).toHaveCount(2)
    await expect(rows.first()).toContainText('1º')
    await expect(rows.first()).toContainText('início 27/10')
    await expect(rows.first()).toContainText('No prazo · +10 d')
    await expect(rows.nth(1)).toContainText('Previsão em cálculo')
    await page.clock.runFor(10_000)
    await expect(page.getByLabel('Visão 3 de 3: Atrasos e riscos')).toBeVisible()
    await expect(page.locator('[data-tv-delay-row]')).toContainText(['OS-2026-C0'])
    await expect(page.locator('[data-tv-delay-row]').first()).toContainText('Atrasada · 3 d')
    await expect(page.locator('[data-tv-delay-row]').first()).toContainText('Qualidade até 04/11')

    // Sem atraso nem risco, a visão sai da rotação.
    api.state.delays = []
    await page.reload()
    await expect(page.getByLabel('Visão 1 de 2: Quadro por etapa')).toBeVisible()
    await page.clock.runFor(20_000)
    await expect(page.getByLabel('Visão 1 de 2: Quadro por etapa')).toBeVisible()
    await expect(page.getByText('Atrasos e riscos')).toHaveCount(0)
  })

  test('apontamento: o cartão muda em menos de 2 s pelo OrderBoardChanged, só com o GET daquele cartão', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    const api = tvApi(['board'])
    const hub = await openTv(page, api)
    await expect(page.getByText('Ao vivo')).toBeVisible()
    const target = page.locator('[data-tv-column="P"] [data-tv-card]').first()
    await expect(target.locator('[data-line="2"]')).toHaveText('João S. +1')
    const boardCalls = api.state.calls.filter((c) => c === 'board').length

    // O operador finaliza a etapa P no app: a OS passa para C com outra pessoa.
    const moved = api.state.cards.findIndex((c) => c.id === 'o-P-0')
    api.state.cards[moved] = card(0, 'P', { id: 'o-P-0', number: 'OS-2026-P0', columnKey: 'C', stepCode: 'C', operator: 'Carla M.', plannedEnd: '2026-10-29' })
    const started = Date.now()
    hub.send('OrderBoardChanged', { productionOrderId: 'o-P-0' })
    await expect(page.locator('[data-tv-column="C"]').getByText('5', { exact: true })).toBeVisible()
    await expect(page.locator('[data-tv-column="P"]').getByText('3', { exact: true })).toBeVisible()
    const elapsed = Date.now() - started
    expect(elapsed, 'commit → OrderBoardChanged → GET do cartão → tela').toBeLessThan(2000)
    expect(api.state.calls).toContain('card:o-P-0')
    expect(api.state.calls.filter((c) => c === 'board').length, 'o quadro inteiro não recarrega').toBe(boardCalls)

    // 50 mudanças em sequência viram uma recarga por cartão (300 ms), sem piscar a tela.
    const before = api.state.calls.length
    for (let i = 0; i < 50; i++) hub.send('OrderBoardChanged', { productionOrderId: 'o-M-1' })
    await expect.poll(() => api.state.calls.slice(before).filter((c) => c === 'card:o-M-1').length).toBe(1)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  })

  test('configuração nova chega pelo KioskConfigChanged sem recarregar a página', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    const api = tvApi(['board'])
    const hub = await openTv(page, api)
    await expect(page.getByText('Ao vivo')).toBeVisible()
    await page.evaluate(() => { (window as unknown as { marker: number }).marker = 42 })
    await expect(page.getByLabel('Visão 1 de 1: Quadro por etapa')).toBeVisible()

    api.state.views = ['queue']
    hub.send('KioskConfigChanged')
    await expect(page.getByLabel('Visão 1 de 1: Fila e previsões')).toBeVisible()
    await expect(page.locator('[data-tv-queue-row]')).toHaveCount(2)
    expect(await page.evaluate(() => (window as unknown as { marker?: number }).marker)).toBe(42)
  })

  test('prévia no backoffice: faixa da tela, visões com os controles e a TV escalada', async ({ page }) => {
    const api = tvApi(['board', 'queue', 'late'], 20)
    await mockApi(page, {
      ...api.preview,
      'GET /api/v1/kiosk-screens': (r: Route) => json(r, {
        screens: [{
          id: 's1', name: 'TV da Montagem', line: 'Linha 1', views: ['board', 'queue', 'late'], rotationSeconds: 20, allowedNetworks: [], tokenPrefix: 'Q2FiZW', createdByName: 'Ana Souza',
          createdAt: new Date().toISOString(), lastSeenAt: new Date().toISOString(), activeSessions: 1, status: 'Active', needsNewLink: false, revokedAt: null, revokedByName: null, version: 2,
        }],
        lines: ['Linha 1'], views: ['board', 'queue', 'late'],
      }),
    })
    await page.route('**/hubs/production/**', (r) => r.fulfill({ status: 503, body: '' }))
    await page.goto('/app/painel-tv')
    await page.getByRole('link', { name: 'Ver prévia' }).click()

    await expect(page).toHaveURL(/\/app\/painel-tv\/s1\/previa$/)
    await expect(page.getByRole('heading', { name: 'Prévia: TV da Montagem' })).toBeVisible()
    await expect(page.getByText('Prévia: esta é a tela TV da Montagem')).toBeVisible()
    const frame = page.locator('[data-preview]')
    await expect(frame.getByRole('heading', { level: 1 })).toHaveText('Linha 1 — 20 OS em andamento, 1 atrasada')
    await expect(frame.locator('[data-tv-column]')).toHaveCount(5)
    await expect(frame).toHaveClass(/theme-tv/)

    await page.getByRole('button', { name: 'Pausar rotação' }).click()
    await expect(page.getByRole('button', { name: 'Retomar rotação' })).toBeVisible()
    await page.getByRole('button', { name: 'Próxima visão' }).click()
    await expect(frame.getByLabel('Visão 2 de 3: Fila e previsões')).toBeVisible()
    await page.getByRole('button', { name: 'Visão anterior' }).click()
    await page.getByRole('button', { name: 'Visão anterior' }).click()
    await expect(frame.getByLabel('Visão 3 de 3: Atrasos e riscos')).toBeVisible()
    expect(api.state.calls).toContain('board')
  })
})
