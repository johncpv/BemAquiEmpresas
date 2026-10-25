import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, hours: number, status: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: hours, status,
  isQualityStep: false, requiresApproval: false, checklist: [],
})
const order = {
  id: 'of1', number: 'OS-2026-00007', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
  promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i1', productCode: 'ELEV-6', productName: 'Elevador 6 paradas', quantity: 1, priority: 0,
  routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte', 12, 'InProgress'), step(2, 'M', 'Montagem', 20, 'Waiting')], totalStandardHours: 32,
  currentStepIndex: 0, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 4,
}
const run = (at: string, over: Record<string, unknown> = {}) => ({
  id: 'r-' + at, trigger: 'Manual', status: 'Succeeded', requestedAt: at, startedAt: at, finishedAt: at, ordersCount: 2, stepsCount: 3, changedOrdersCount: 1, withoutForecastCount: 1,
  durationMs: 180, error: null, ...over,
})
const forecast = {
  orderId: 'of1', plannedStart: '2026-10-30', plannedEnd: '2026-11-05', noForecastReason: null,
  steps: [
    { stepId: 's1', sequence: 1, hours: 2, plannedStart: '2026-10-30', plannedEnd: '2026-11-03', noForecastReason: null },
    { stepId: 's2', sequence: 2, hours: 20, plannedStart: '2026-11-04', plannedEnd: '2026-11-05', noForecastReason: null },
  ],
}

async function planningApi(page: import('@playwright/test').Page, state: { summary: Record<string, unknown> }, overrides: Record<string, (r: Route) => unknown> = {}, actions = ALL) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions } as never }) }),
    'GET /api/v1/production/schedule': (r: Route) => json(r, state.summary),
    'GET /api/v1/production/schedule/orders': (r: Route) => json(r, new URL(r.request().url()).searchParams.getAll('ids').includes('of1') ? [forecast] : []),
    'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    ...overrides,
  })
}

test.describe('Planejamento (PLN-01a)', () => {
  test('recalcula, mostra a OS sem previsão com a ação que resolve e a data prevista na OS', async ({ page }) => {
    const state = {
      summary: {
        lastRun: run('2026-10-30T13:00:00Z', { trigger: 'Hourly' }), lastSuccess: run('2026-10-30T13:00:00Z', { trigger: 'Hourly' }), pending: false, openOrders: 2,
        warnings: [{ orderId: 'of2', orderNumber: 'OS-2026-00008', reason: 'Etapa Solda sem centro de trabalho', action: 'SetWorkCenter' }],
      } as Record<string, unknown>,
    }
    let recalculated = 0
    await planningApi(page, state, {
      'POST /api/v1/production/schedule/recalculate': async (r: Route) => {
        recalculated++
        await new Promise((resolve) => setTimeout(resolve, 300))
        const done = run('2026-10-30T17:05:00Z')
        state.summary = { ...state.summary, lastRun: done, lastSuccess: done }
        return json(r, { ran: true, queued: false, run: done })
      },
    })

    await page.goto('/app/producao/planejamento')
    await expect(page.getByRole('heading', { name: 'Produção: Planejamento' })).toBeVisible()
    await expect(page.getByText('Recalculado às 10:00 (de hora em hora), 2 OS em 180 ms')).toBeVisible()
    await expect(page.getByText('Sem previsão: Etapa Solda sem centro de trabalho')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Informar centro' })).toHaveAttribute('href', /\/app\/producao\/os\/of2\?aba=etapas/)

    const button = page.getByRole('button', { name: 'Recalcular agora' })
    const box = await button.boundingBox()
    expect(box!.height).toBeGreaterThanOrEqual(44)
    await button.click()
    await expect(page.getByRole('button', { name: 'Recalculando…' })).toBeVisible()
    await expect(page.getByText('Recalculado às 14:05 (manual), 2 OS em 180 ms').first()).toBeVisible()
    expect(recalculated).toBe(1)

    // CA 16: a OS mostra "Fim previsto" e cada etapa o "Previsto" e as horas restantes, lidos do plano.
    await page.goto('/app/producao/os/of1')
    await expect(page.getByText('05/11/2026').first()).toBeVisible()
    await expect(page.getByText('Previsto 30/10/2026 → 03/11/2026')).toBeVisible()
    await expect(page.getByText('Horas restantes 2 h')).toBeVisible()
    await expect(page.getByText('Previsto 04/11/2026 → 05/11/2026')).toBeVisible()
    const width = await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
    expect(width, 'sem rolagem horizontal').toBe(true)
  })

  test('sem production.edit o botão some; sem OS aberta, o vazio leva às OS; erro do último cálculo', async ({ page }) => {
    const state = { summary: { lastRun: null, lastSuccess: null, pending: false, openOrders: 0, warnings: [] } as Record<string, unknown> }
    await planningApi(page, state, {}, ['view'])
    await page.goto('/app/producao/planejamento')
    await expect(page.getByText('Nenhuma OS de fabricação aberta para planejar.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver OS de fabricação' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Recalcular agora' })).toHaveCount(0)

    state.summary = {
      lastRun: run('2026-10-30T17:10:00Z', { status: 'Failed', error: 'timeout' }), lastSuccess: run('2026-10-30T16:00:00Z', { trigger: 'Hourly' }), pending: false, openOrders: 3, warnings: [],
    }
    await page.reload()
    await expect(page.getByRole('alert').getByText('O último cálculo falhou às 14:10. As datas mostradas são do cálculo anterior.')).toBeVisible()
    await expect(page.getByText('Recalculado às 13:00 (de hora em hora), 2 OS em 180 ms')).toBeVisible()
  })
})
