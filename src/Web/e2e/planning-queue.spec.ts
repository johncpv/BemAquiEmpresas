import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']

const row = (id: string, number: string, over: Record<string, unknown> = {}) => ({
  position: 0, projectId: id, projectNumber: number, projectName: `Projeto ${number}`, partyId: 'c-' + id, partyName: `Cliente ${number}`, openOrders: 2, remainingHours: 24,
  promisedDate: '2026-12-05', forecastDate: '2026-12-01', slackDays: 4, risk: 'None', hasForecast: true, noForecastReason: null, reasonOrderId: null, reasonOrderNumber: null,
  ...over,
})

const ROWS = {
  p1: row('p1', 'PRJ-2026-0001', { promisedDate: '2026-12-05', forecastDate: '2026-12-05', slackDays: 0 }),
  p2: row('p2', 'PRJ-2026-0002', { promisedDate: '2026-12-10', forecastDate: '2026-12-08', slackDays: 2 }),
  p3: row('p3', 'PRJ-2026-0003', { promisedDate: '2026-12-01', forecastDate: '2026-12-12', slackDays: -11, risk: 'AtRisk' }),
}

type Id = keyof typeof ROWS

/** Prévia: quem sobe adianta 4 dias; quem desce atrasa 4 dias (o p1 passa do prometido). */
function previewFor(order: string[], saved: string[]) {
  return {
    durationMs: 120,
    rows: order.map((id, i) => {
      const r = ROWS[id as Id]
      const before = saved.indexOf(id)
      const shift = (i - before) * 4
      const add = (date: string, days: number) => new Date(Date.parse(date + 'T12:00:00Z') + days * 86_400_000).toISOString().slice(0, 10)
      const after = add(r.forecastDate, shift)
      const late = Math.round((Date.parse(after) - Date.parse(r.promisedDate)) / 86_400_000)
      return {
        projectId: id, positionBefore: before + 1, positionAfter: i + 1, promisedDate: r.promisedDate, forecastBefore: r.forecastDate, forecastAfter: after,
        slackBefore: r.slackDays, slackAfter: -late, riskBefore: r.risk, riskAfter: late > 0 ? 'AtRisk' : 'None', worsened: late > 0 && shift > 0, daysLate: late > 0 ? late : null,
      }
    }),
  }
}

async function queueApi(page: Page, state: { order: string[]; version: number }, options: { actions?: string[]; saveStatus?: number; previews?: string[][] } = {}) {
  const view = () => ({ version: state.version, reorderedAt: null, reorderedBy: null, rows: state.order.map((id, i) => ({ ...ROWS[id as Id], position: i + 1 })) })
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions: options.actions ?? ALL } as never }) }),
    'GET /api/v1/production/schedule': (r: Route) => json(r, { lastRun: null, lastSuccess: null, pending: false, openOrders: 6, warnings: [] }),
    'GET /api/v1/production/planning/queue': (r: Route) => json(r, view()),
    'GET /api/v1/production/planning/queue/changes': (r: Route) => json(r, {
      items: [{ id: 'c1', at: '2026-10-30T17:00:00Z', who: 'Sistema', kind: 'Entered', reason: 'Entrou pela data prometida', moves: [{ projectId: 'p3', projectNumber: 'PRJ-2026-0003', from: null, to: 3 }] }],
      page: 1, pageSize: 20, total: 1,
    }),
    'POST /api/v1/production/planning/queue/preview': async (r: Route) => {
      const order = (r.request().postDataJSON() as { order: string[] }).order
      options.previews?.push(order)
      return json(r, previewFor(order, state.order))
    },
    'PUT /api/v1/production/planning/queue': async (r: Route) => {
      const body = r.request().postDataJSON() as { order: string[]; version: number }
      if (options.saveStatus === 409) return json(r, { code: 'concurrency_conflict', detail: 'A fila mudou desde que você abriu. Recarregue.' }, 409)
      state.order = body.order
      state.version++
      return json(r, { changed: true, recalculationRequested: true, queue: view() })
    },
  })
}

const linhas = (page: Page) => page.getByTestId('fila-linha')
const numbers = async (page: Page) => linhas(page).evaluateAll((els) => els.map((e) => e.getAttribute('data-project')))

test.describe('Fila de prioridade (PLN-02)', () => {
  test('arrasta um projeto, vê a prévia, confirma quem atrasa e salva', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px não há arraste (teste próprio)')
    const state = { order: ['p1', 'p2', 'p3'], version: 7 }
    const previews: string[][] = []
    await queueApi(page, state, { previews })
    await page.goto('/app/producao/planejamento')
    await expect(page.getByRole('tab', { name: 'Fila' })).toHaveAttribute('aria-selected', 'true')
    await expect(linhas(page)).toHaveCount(3)
    expect(await numbers(page)).toEqual(['PRJ-2026-0001', 'PRJ-2026-0002', 'PRJ-2026-0003'])
    await expect(linhas(page).nth(2).getByText('Em risco').last()).toBeVisible()

    // Arrasta o PRJ-3 pela alça até o lugar do PRJ-1.
    const handle = page.getByRole('button', { name: 'Arrastar PRJ-2026-0003' })
    const target = page.getByRole('button', { name: 'Arrastar PRJ-2026-0001' })
    const from = (await handle.boundingBox())!
    const to = (await target.boundingBox())!
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2)
    await page.mouse.down()
    for (let i = 1; i <= 12; i++) await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2 + ((to.y - from.y - 10) * i) / 12)
    await page.mouse.up()

    await expect(page.getByText('Ordem alterada, ainda não salva.')).toBeVisible()
    expect(await numbers(page)).toEqual(['PRJ-2026-0003', 'PRJ-2026-0001', 'PRJ-2026-0002'])
    // CA 4: em até 2 s, a prévia antes → depois em cada linha; quem passa a atrasar fica destacado.
    await expect(linhas(page).nth(1).getByTestId('fila-previa')).toContainText('Previsto 05/12 → 09/12', { timeout: 2000 })
    await expect(linhas(page).nth(1).getByTestId('fila-previa')).toContainText('Passa a atrasar 4 dias')
    await expect(linhas(page).nth(0).getByTestId('fila-previa')).toContainText('12/12 → 04/12')
    expect(previews.at(-1)).toEqual(['p3', 'p1', 'p2'])

    // CA 5: o diálogo lista quem passa a atrasar; "Salvar mesmo assim" grava com a versão.
    await page.getByRole('button', { name: 'Salvar ordem' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText(/Estes projetos passam a atrasar: PRJ-2026-0001 \(4 dias depois do prometido\), PRJ-2026-0002 \(2 dias depois do prometido\)\./)).toBeVisible()
    await dialog.getByRole('button', { name: 'Salvar mesmo assim' }).click()
    await expect(page.getByText('Ordem salva. Recalculando as datas…')).toBeVisible()
    expect(state.order).toEqual(['p3', 'p1', 'p2'])
    await expect(page.getByText('Ordem alterada, ainda não salva.')).toHaveCount(0)
    expect(await numbers(page)).toEqual(['PRJ-2026-0003', 'PRJ-2026-0001', 'PRJ-2026-0002'])

    // CA 8: mudanças na fila, com quem (ou "Sistema") e as posições.
    await page.getByRole('button', { name: 'Mudanças na fila' }).click()
    await expect(page.getByText('Entrou pela data prometida')).toBeVisible()
    await expect(page.getByText('PRJ-2026-0003: entrou na 3')).toBeVisible()
  })

  test('reordena só pelo teclado, anuncia a posição e descarta', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px não há arraste (teste próprio)')
    const state = { order: ['p1', 'p2', 'p3'], version: 1 }
    await queueApi(page, state)
    await page.goto('/app/producao/planejamento?aba=fila')
    await expect(linhas(page)).toHaveCount(3)

    // dnd-kit com o sensor de teclado: Espaço pega, seta move, Espaço solta.
    const handle = page.getByRole('button', { name: 'Arrastar PRJ-2026-0002' })
    await handle.focus()
    await page.keyboard.press('Space')
    // Com a máquina carregada, a seta chegava antes de o dnd-kit pegar o item; espera o arraste começar.
    await expect(handle).toHaveAttribute('aria-pressed', 'true')
    await page.keyboard.press('ArrowUp')
    await page.keyboard.press('Space')
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0002', 'PRJ-2026-0001', 'PRJ-2026-0003'])
    await expect(page.getByText('Ordem alterada, ainda não salva.')).toBeVisible()

    // Os botões de mover, com Tab e Enter.
    await page.getByRole('button', { name: 'Mover PRJ-2026-0003 para cima' }).focus()
    await page.keyboard.press('Enter')
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0002', 'PRJ-2026-0003', 'PRJ-2026-0001'])
    await expect(page.getByRole('status').filter({ hasText: 'Projeto PRJ-2026-0003 movido para a posição 2' })).toHaveCount(1)

    // "Mover para a posição…" pelo teclado.
    await page.getByRole('button', { name: 'Mover PRJ-2026-0001 para a posição…' }).focus()
    await page.keyboard.press('Enter')
    await page.getByRole('spinbutton', { name: 'Mover PRJ-2026-0001 para a posição' }).fill('1')
    await page.keyboard.press('Enter')
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0001', 'PRJ-2026-0002', 'PRJ-2026-0003'])
    await expect(page.getByText('Ordem alterada, ainda não salva.')).toHaveCount(0, { timeout: 2000 })

    await page.getByRole('button', { name: 'Mover PRJ-2026-0001 para baixo' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Ordem alterada, ainda não salva.')).toBeVisible()
    await page.getByRole('button', { name: 'Descartar' }).click()
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0001', 'PRJ-2026-0002', 'PRJ-2026-0003'])
    await expect(page.getByText('Ordem alterada, ainda não salva.')).toHaveCount(0)
    expect(state.version).toBe(1)
  })

  test('a 400 px, sem arraste: Subir e Descer com 44 px e a prévia abaixo da linha', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const state = { order: ['p1', 'p2', 'p3'], version: 3 }
    await queueApi(page, state)
    await page.goto('/app/producao/planejamento')
    await expect(linhas(page)).toHaveCount(3)
    await expect(page.getByRole('button', { name: 'Arrastar PRJ-2026-0001' })).toBeHidden()
    const up = page.getByRole('button', { name: 'Subir PRJ-2026-0002' })
    const box = (await up.boundingBox())!
    expect(box.height).toBeGreaterThanOrEqual(44)
    await expect(linhas(page).nth(0).getByText('Previsto 05/12/2026')).toBeVisible()
    await up.click()
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0002', 'PRJ-2026-0001', 'PRJ-2026-0003'])
    await expect(linhas(page).nth(1).getByTestId('fila-previa')).toContainText('05/12 → 09/12')
    await page.getByRole('button', { name: 'Descer PRJ-2026-0002' }).click()
    await expect.poll(() => numbers(page)).toEqual(['PRJ-2026-0001', 'PRJ-2026-0002', 'PRJ-2026-0003'])
    const fits = await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
    expect(fits, 'sem rolagem horizontal').toBe(true)
  })

  test('sem production.edit a fila é só leitura; conflito ao salvar pede para recarregar', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'os botões de mover do desktop; a 400 px há teste próprio')
    const state = { order: ['p1', 'p2', 'p3'], version: 2 }
    await queueApi(page, state, { actions: ['view'] })
    await page.goto('/app/producao/planejamento')
    await expect(page.getByText('Para mudar a ordem, peça a quem cuida da produção.')).toBeVisible()
    await expect(page.getByRole('button', { name: /Arrastar|Mover|Subir|Descer/ })).toHaveCount(0)

    await page.unrouteAll({ behavior: 'ignoreErrors' })
    await queueApi(page, state, { saveStatus: 409 })
    await page.reload()
    await page.getByRole('button', { name: 'Mover PRJ-2026-0001 para baixo' }).click()
    await expect(linhas(page).nth(1).getByTestId('fila-previa')).toBeVisible()
    await page.getByRole('button', { name: 'Salvar ordem' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Salvar mesmo assim' }).click()
    await expect(page.getByRole('alertdialog').getByText('A fila mudou desde que você abriu. Recarregue.')).toBeVisible()
    await expect(page.getByRole('alertdialog').getByRole('button', { name: 'Recarregar' })).toBeVisible()
  })

  test('fila vazia leva às OS de fabricação', async ({ page }) => {
    await queueApi(page, { order: [], version: 0 })
    await page.goto('/app/producao/planejamento')
    await expect(page.getByText('Nenhum projeto com OS de fabricação aberta.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver OS de fabricação' })).toBeVisible()
  })
})
