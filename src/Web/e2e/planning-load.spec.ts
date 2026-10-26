import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']

const week = (start: string, end: string, isoWeek: number, from = start) => ({ start, end, isoWeek, from })
const cell = (weekStart: string, committedHours: number, capacityHours: number, percent: number | null, band: string) => ({ weekStart, committedHours, capacityHours, percent, band })

const WEEKS = [week('2026-10-26', '2026-11-01', 44, '2026-10-30'), week('2026-11-02', '2026-11-08', 45), week('2026-11-09', '2026-11-15', 46)]

/** O cenário fixo da PLN-01a (Corte e Montagem) e uma semana em que a capacidade do Corte caiu depois do plano. */
function loadView(over = false) {
  const cut = {
    workCenterId: 'cut', name: 'Corte', line: 'Cabines', hoursAfter: 320, busyUntil: '2027-02-19',
    cells: [cell(WEEKS[0].start, 8, 8, 100, 'Full'), cell(WEEKS[1].start, 10, 32, 31.3, 'Idle'), over ? cell(WEEKS[2].start, 40, 32, 125, 'Over') : cell(WEEKS[2].start, 39, 40, 97.5, 'Full')],
  }
  const assembly = {
    workCenterId: 'asm', name: 'Montagem', line: 'Cabines', hoursAfter: 0, busyUntil: '2026-11-05',
    cells: [cell(WEEKS[0].start, 0, 16, 0, 'Idle'), cell(WEEKS[1].start, 34, 64, 53.1, 'Idle'), cell(WEEKS[2].start, 56, 80, 70, 'Normal')],
  }
  const factory = {
    workCenterId: null, name: 'Fábrica', line: null, hoursAfter: 320, busyUntil: '2027-02-19',
    cells: [cell(WEEKS[0].start, 8, 24, 33.3, 'Idle'), cell(WEEKS[1].start, 44, 96, 45.8, 'Idle'), over ? cell(WEEKS[2].start, 96, 112, 85.7, 'Normal') : cell(WEEKS[2].start, 95, 120, 79.2, 'Normal')],
  }
  return { load: { today: '2026-10-30', planComputedAt: '2026-10-30T17:05:00Z', weeks: WEEKS, centers: [cut, assembly], factory }, lines: ['Cabines'], activeCenters: 2 }
}

const CELL = {
  workCenterId: 'asm', name: 'Montagem', weekStart: '2026-11-02', from: '2026-11-02', to: '2026-11-08', totalHours: 34,
  steps: [
    { stepId: 's2', stepName: 'Montagem', orderId: 'o2', orderNumber: 'OS-2026-0002', projectId: 'p2', projectNumber: 'PRJ-2026-0001', projectName: 'Ed. Aurora', workCenterId: 'asm', workCenterName: 'Montagem', hours: 10, plannedStart: '2026-11-03', plannedEnd: '2026-11-03' },
    { stepId: 's1', stepName: 'Montagem', orderId: 'o1', orderNumber: 'OS-2026-0001', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', workCenterId: 'asm', workCenterName: 'Montagem', hours: 20, plannedStart: '2026-11-04', plannedEnd: '2026-11-05' },
    { stepId: 's3', stepName: 'Montagem', orderId: 'o3', orderNumber: 'OS-2026-0003', projectId: 'p3', projectNumber: 'PRJ-2026-0002', projectName: 'Ed. Solar', workCenterId: 'asm', workCenterName: 'Montagem', hours: 4, plannedStart: '2026-11-05', plannedEnd: '2026-11-05' },
  ],
}

async function loadApi(page: Page, options: { over?: boolean; actions?: string[]; empty?: boolean; noCenters?: boolean; loads?: string[]; recalculated?: string[] } = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions: options.actions ?? ALL } as never }) }),
    'GET /api/v1/production/schedule': (r: Route) => json(r, { lastRun: null, lastSuccess: null, pending: false, openOrders: 3, warnings: [] }),
    'GET /api/v1/production/planning/queue': (r: Route) => json(r, { version: 1, reorderedAt: null, reorderedBy: null, rows: [] }),
    'GET /api/v1/production/planning/load': (r: Route) => {
      options.loads?.push(r.request().url())
      const view = loadView(options.over)
      if (options.noCenters) return json(r, { ...view, load: { ...view.load, centers: [], factory: { ...view.load.factory, hoursAfter: 0, cells: [] } }, activeCenters: 0 })
      if (options.empty) {
        const zero = (row: { cells: { committedHours: number; percent: number | null; band: string }[]; hoursAfter: number; busyUntil: string | null }) =>
          ({ ...row, hoursAfter: 0, busyUntil: null, cells: row.cells.map((c) => ({ ...c, committedHours: 0, percent: 0, band: 'Idle' })) })
        return json(r, { ...view, load: { ...view.load, centers: view.load.centers.map(zero), factory: zero(view.load.factory) } })
      }
      return json(r, view)
    },
    'GET /api/v1/production/planning/load/cell': (r: Route) => json(r, CELL),
    'POST /api/v1/production/schedule/recalculate': (r: Route) => {
      options.recalculated?.push('ok')
      return json(r, { ran: true, queued: false, run: { id: 'r1', trigger: 'Manual', status: 'Succeeded', requestedAt: '2026-10-30T17:10:00Z', startedAt: '2026-10-30T17:10:00Z', finishedAt: '2026-10-30T17:10:01Z', ordersCount: 3, stepsCount: 6, changedOrdersCount: 1, withoutForecastCount: 0, durationMs: 80, error: null } })
    },
  })
}

test.describe('Ocupação da fábrica (PLN-04)', () => {
  test('abre a Ocupação, navega pelo teclado, abre a célula e troca para o gráfico', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    const loads: string[] = []
    await loadApi(page, { loads })
    await page.goto('/app/producao/planejamento?aba=ocupacao')
    await expect(page.getByRole('tab', { name: 'Ocupação' })).toHaveAttribute('aria-selected', 'true')
    const grid = page.getByRole('treegrid', { name: 'Ocupação por semana e centro' })
    await expect(grid.getByRole('columnheader', { name: 'S45 · 02/11' })).toBeVisible()
    await expect(grid.getByRole('gridcell', { name: 'Fábrica', exact: true })).toBeVisible()
    await expect(page.getByText('Plano de 14:05.')).toBeVisible()

    // CA 2 e 6: os números do dataset fixo, com o estado escrito.
    await expect(grid.getByRole('gridcell', { name: 'Corte, S44 · 26/10: 8 de 8 h, 100,0%, Lotado' })).toBeVisible()
    await expect(grid.getByRole('gridcell', { name: 'Montagem, S45 · 02/11: 34 de 64 h, 53,1%, Folga' })).toBeVisible()

    // CA 3: "Depois de" com o último dia planejado.
    await expect(page.getByRole('heading', { name: /Depois de 15\/11/ })).toBeVisible()
    await expect(page.getByTestId('depois-de')).toContainText('Corte ocupado até 19/02/2027')

    // CA 7 e 5: setas e Enter abrem o drill-down com o total igual ao da célula.
    await grid.getByRole('gridcell', { name: /^Corte, S44/ }).focus()
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('ArrowRight')
    await expect(grid.getByRole('gridcell', { name: /^Montagem, S45/ })).toBeFocused()
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog', { name: 'Montagem · S45 · 02/11' })
    await expect(panel.getByTestId('celula-total')).toHaveText('Total da semana: 34 h')
    await expect(panel.getByRole('link', { name: 'OS-2026-0001' })).toBeVisible()
    await expect(panel.getByText('Previsto 04/11/2026 → 05/11/2026')).toBeVisible()
    await expect(panel.getByRole('link', { name: 'Ver a fila' })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(panel).toBeHidden()

    // CA 8 e 9: gráfico com o resumo, a referência e a tabela equivalente; os filtros vão para a URL.
    await page.getByRole('button', { name: 'Gráfico' }).click()
    await expect(page).toHaveURL(/visao=grafico/)
    await expect(page.getByTestId('ocupacao-resumo')).toHaveText('Semana mais ocupada: S46 · 09/11, 79,2%. Corte ocupado até 19/02/2027.')
    await expect(page.getByRole('img', { name: 'Ocupação da fábrica por semana' })).toBeVisible()
    await expect(page.getByText('Capacidade (100%)').last()).toBeVisible()
    await page.getByRole('combobox', { name: /^Centro/ }).selectOption('cut')
    await expect(page).toHaveURL(/centro=cut/)
    await expect(page.getByTestId('ocupacao-resumo')).toHaveText('Semana mais ocupada: S44 · 26/10, 100,0%. Corte ocupado até 19/02/2027.')
    await page.getByRole('combobox', { name: /^Semanas/ }).selectOption('26')
    await expect(page).toHaveURL(/semanas=26/)
    await expect.poll(() => loads.at(-1)).toContain('weeks=26')
    await page.reload()
    await expect(page.getByRole('button', { name: 'Gráfico' })).toHaveAttribute('aria-pressed', 'true')
    await expect(page.getByRole('combobox', { name: /^Centro/ })).toHaveValue('cut')
  })

  test('acima da capacidade mostra o crítico com "Recalcular agora"', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    const recalculated: string[] = []
    await loadApi(page, { over: true, recalculated })
    await page.goto('/app/producao/planejamento?aba=ocupacao')
    const cellOver = page.getByRole('gridcell', { name: 'Corte, S46 · 09/11: 40 de 32 h, 125,0%, Acima da capacidade' })
    await expect(cellOver.locator('[data-band]')).toHaveAttribute('data-band', 'Over')
    const alert = page.getByRole('alert').filter({ hasText: 'Acima da capacidade' })
    await expect(alert).toContainText('em 1 semana')
    await alert.getByRole('button', { name: 'Recalcular agora' }).click()
    await expect(page.getByText(/Recalculado às/)).toBeVisible()
    expect(recalculated).toHaveLength(1)
  })

  test('sem production.edit não há "Recalcular agora"; vazio leva à fila', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    await loadApi(page, { over: true, actions: ['view'] })
    await page.goto('/app/producao/planejamento?aba=ocupacao')
    await expect(page.getByRole('alert').filter({ hasText: 'Acima da capacidade' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Recalcular agora' })).toHaveCount(0)

    await page.unrouteAll({ behavior: 'ignoreErrors' })
    await loadApi(page, { empty: true })
    await page.reload()
    await expect(page.getByText('Nenhuma etapa planejada no período.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver a fila' })).toBeVisible()

    await page.unrouteAll({ behavior: 'ignoreErrors' })
    await loadApi(page, { noCenters: true })
    await page.reload()
    await expect(page.getByText('Cadastre os centros de trabalho para ver a ocupação.')).toBeVisible()
    await expect(page.getByRole('tabpanel', { name: 'Ocupação' }).getByRole('link', { name: 'Centros de trabalho' })).toBeVisible()
  })

  test('a 400 px: uma semana por vez, centros em lista e botões de 44 px', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    await loadApi(page)
    await page.goto('/app/producao/planejamento?aba=ocupacao')
    const items = page.getByTestId('ocupacao-centro')
    await expect(items).toHaveCount(3)
    await expect(page.getByRole('treegrid')).toBeHidden()
    await expect(items.nth(0)).toContainText('100,0%')
    const next = page.getByRole('button', { name: 'Próxima semana' })
    expect((await next.boundingBox())!.height).toBeGreaterThanOrEqual(44)
    await next.click()
    await expect(items.nth(1)).toContainText('34 de 64 h')
    await expect(page).toHaveURL(/semana=2026-11-02/)
    await items.nth(1).click()
    await expect(page.getByRole('dialog', { name: 'Montagem · S45 · 02/11' }).getByTestId('celula-total')).toHaveText('Total da semana: 34 h')
    await page.keyboard.press('Escape')
    await page.getByRole('button', { name: 'Gráfico' }).click()
    await expect(page.getByRole('img', { name: 'Ocupação da fábrica por semana' })).toBeVisible()
    const fits = await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
    expect(fits, 'sem rolagem horizontal').toBe(true)
  })
})
