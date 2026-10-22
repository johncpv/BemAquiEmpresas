import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const center = { id: 'c1', name: 'Montagem', line: 'Linha 1', shifts: [], hoursPerDay: 8, calendarId: 'cal', isActive: true, createdAt: '2026-10-21T12:00:00Z' }
const step = (i: number, code: string, name: string, hours: number) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: hours, status: i === 1 ? 'Ready' : 'Waiting',
  isQualityStep: name === 'Qualidade', requiresApproval: false, checklist: [],
})
const order = (steps: ReturnType<typeof step>[], version = 1) => ({
  id: 'of1', number: 'OS-2026-00007', status: 'Open', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
  promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i1', productCode: 'ELEV-6', productName: 'Elevador 6 paradas', quantity: 1, priority: 0,
  routingTemplateId: steps.length ? 'rt1' : null, routingTemplateName: steps.length ? 'Elevador padrão' : null, steps, totalStandardHours: steps.reduce((s, x) => s + x.standardHours, 0),
  currentStepIndex: steps.length ? 0 : -1, started: false, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version,
})
const summary = { id: 'of1', number: 'OS-2026-00007', status: 'Open', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
  projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productName: 'Elevador 6 paradas', quantity: 1, promisedDate: '2026-10-01', delayDays: 20, codes: [], stepNames: [], currentStepIndex: -1, noRouting: true }

async function fabricationApi(page: import('@playwright/test').Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never }) }),
    'GET /api/v1/production/work-centers': (r: Route) => json(r, { items: [center], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/production/production-orders': (r: Route) => json(r, { items: [summary], page: 1, pageSize: 20, total: 1 }),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    ...overrides,
  })
}

test.describe('OS de fabricação (PRD-01)', () => {
  test('a OS nascida sem roteiro ganha as etapas pelo "Trocar roteiro" e as etapas são ajustadas e salvas', async ({ page }) => {
    let current = order([])
    let saved: Record<string, unknown> | undefined
    await fabricationApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, current),
      'GET /api/v1/production/production-orders/of1/routings': (r: Route) => json(r, [
        { id: 'rt1', name: 'Elevador padrão', productType: 'Elevador', stepCount: 3, totalStandardHours: 6, suggested: true, hasAllWorkCenters: true },
        { id: 'rt2', name: 'Porta', productType: 'Porta', stepCount: 2, totalStandardHours: 3, suggested: false, hasAllWorkCenters: true },
      ]),
      'POST /api/v1/production/production-orders/of1/routing': (r: Route) => { current = order([step(1, 'C', 'Corte', 2), step(2, 'M', 'Montagem', 3), step(3, 'Q', 'Qualidade', 1)], 2); return json(r, current) },
      'PUT /api/v1/production/production-orders/of1/steps': (r: Route) => { saved = r.request().postDataJSON(); current = order([step(1, 'C', 'Corte', 2.5), step(2, 'M', 'Montagem', 3), step(3, 'Q', 'Qualidade', 1)], 3); return json(r, current) },
    })

    // CA 15: a lista mostra o chip "Sem roteiro" e a faixa "Atrasada 20 d"; a primeira coluna leva ao detalhe.
    await page.goto('/app/producao/os')
    await expect(page.getByText('Atrasada 20 d')).toBeVisible()
    await expect(page.getByText('Sem roteiro').first()).toBeVisible()
    await page.getByRole('link', { name: 'OS-2026-00007' }).click()

    // CA 4 e 9: escolher o roteiro sugerido, com o impacto antes → depois.
    await expect(page.getByRole('heading', { name: 'OS-2026-00007 — Aurora — Elevador' })).toBeVisible()
    await expect(page.getByText('Escolha o roteiro desta OS.', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Escolher roteiro' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Sugerido')).toBeVisible()
    await dialog.getByRole('radio').first().check()
    await expect(dialog.getByText('0 → 3 etapas · 0,00 h → 6,00 h padrão')).toBeVisible()
    await dialog.getByRole('button', { name: 'Trocar roteiro' }).click()
    await expect(page.getByText('Roteiro trocado')).toBeVisible()

    // CA 7: ajustar as horas e salvar as etapas.
    await expect(page.getByLabel('Nome da etapa 1')).toHaveValue('Corte')
    await page.getByLabel('Horas padrão da etapa 1').fill('2,5')
    await page.getByRole('button', { name: 'Salvar etapas' }).click()
    await expect(page.getByText('Etapas salvas')).toBeVisible()
    expect(saved).toMatchObject({ version: 2, steps: [{ id: 's1', code: 'C', standardHours: 2.5 }, { id: 's2' }, { id: 's3', isQualityStep: true }] })
  })

  test('gera a OS do componente pelo botão no projeto e oferece o próximo passo', async ({ page }) => {
    let body: unknown
    await fabricationApi(page, {
      'GET /api/v1/projects/p1': (r: Route) => json(r, {
        id: 'p1', number: 'PRJ-2026-0003', name: 'Aurora — Elevador', status: 'Planned', partyId: 'cli1', partyName: 'Condomínio Aurora', contractId: 'k1', contractNumber: 'CTR-2026-0003',
        siteAddressId: null, siteAddress: null, promisedDate: '2026-12-20', forecastDate: null, priority: null, managerUserId: null, managerName: null, createdAt: '2026-10-21T12:00:00Z',
        items: [{ id: 'pi2', sequence: 2, description: 'Porta de cabine', quantity: 2, itemId: null, quoteItemId: 'q2' }], version: 1, fabricatedOn: null, deliveredOn: null,
        deliveredManually: false, hasFabricationMilestone: false, contractStatus: 'Active', isOnboarding: false,
      }),
      'GET /api/v1/production/production-orders': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
      'GET /api/v1/production/production-orders/pending-items': (r: Route) => json(r, { items: [{ projectItemId: 'pi2', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora', partyName: 'Condomínio Aurora', sequence: 2, description: 'Porta de cabine', quantity: 2, kind: 'Product', productId: null, productName: null, promisedDate: '2026-12-20' }], page: 1, pageSize: 100, total: 1 }),
      'GET /api/v1/production/installations': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
      'POST /api/v1/production/production-orders': (r: Route) => { body = r.request().postDataJSON(); return json(r, { ...order([step(1, 'C', 'Corte', 1)]), projectItemId: 'pi2', itemDescription: 'Porta de cabine', productName: null, productCode: null, quantity: 2 }, 201) },
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, { ...order([step(1, 'C', 'Corte', 1)]), projectItemId: 'pi2', itemDescription: 'Porta de cabine', productName: null, productCode: null, quantity: 2 }),
    })
    await page.goto('/app/projetos/p1?aba=itens')
    await page.getByRole('button', { name: 'Gerar OS de fabricação' }).click()
    await expect(page).toHaveURL(/\/app\/producao\/os\/of1/)
    expect(body).toEqual({ projectItemId: 'pi2' })
    await expect(page.getByRole('button', { name: 'Ajustar as etapas' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Ver projeto' })).toBeVisible()
  })

  test('a 400 px a lista e o detalhe cabem sem rolar para o lado', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    await fabricationApi(page, { 'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, { ...order([step(1, 'P', 'Projeto', 4), step(2, 'C', 'Corte', 2), step(3, 'M', 'Montagem', 3)]), started: true, status: 'InProgress' }) })
    await page.goto('/app/producao/os')
    await expect(page.getByRole('link', { name: 'OS-2026-00007' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
    await page.goto('/app/producao/os/of1')
    await expect(page.getByText('A OS já começou. As etapas seguem o roteiro gravado.')).toBeVisible()
    await expect(page.getByRole('list', { name: 'Etapas da OS' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
  })
})