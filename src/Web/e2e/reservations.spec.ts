import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, status: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 0, awaitingApproval: false, selfApproved: false, selfInspection: false,
})
const order = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1',
  partyName: 'Condomínio Aurora', promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i9', productCode: 'ELEV-6', productName: 'Elevador 6 paradas',
  quantity: 1, priority: 0, routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [step(1, 'C', 'Corte', 'Done'), step(2, 'M', 'Montagem', 'Ready')], totalStandardHours: 4,
  currentStepIndex: 1, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 7, qualityAttempt: 1, selfInspection: false,
  readyToComplete: false, openNonConformities: 0, completedAt: null, completedByName: null, completionBlocker: 'Falta concluir a etapa Montagem.', completionReversedAt: null,
  completionReversedByName: null, completionReversalReason: null,
}
const location = (reserved: number, shortage: number) => ({ reservationId: 'r1', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', requested: 10, reserved, issued: 0, shortage, status: 'Active' })
const cable = (reserved: number, shortage: number) => ({
  itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', bomLineId: 'b1', required: 10, reserved, issued: 0, shortage, overIssued: 0, isBackfill: false,
  locations: [location(reserved, shortage)], purchaseRequestLink: null,
})
const guide = {
  itemId: 'i2', itemCode: 'MP-002', itemName: 'Guia T 50', unitCode: 'UN', bomLineId: 'b2', required: 4, reserved: 4, issued: 0, shortage: 0, overIssued: 0, isBackfill: true,
  locations: [{ reservationId: 'r2', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', requested: 4, reserved: 4, issued: 0, shortage: 0, status: 'Active' }], purchaseRequestLink: null,
}
const material = (reconciling: boolean) => ({
  productionOrderId: 'of1', bomReleased: true, bomRevision: 1, bomReleasedAt: '2026-10-23T12:00:00Z', reconciling, orderClosed: false,
  totalShortage: reconciling ? 0 : 4, totalOverIssued: 0, lines: reconciling ? [{ ...cable(0, 0), required: 10, locations: [] }, { ...guide, reserved: 0, isBackfill: false, locations: [] }] : [cable(6, 4), guide],
})
const history = {
  items: [{ id: 'h1', productionOrderId: 'of1', orderNumber: 'OS-2026-00041', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', locationId: 'alm', locationCode: 'ALM',
    kind: 'Reserved', quantityDelta: 6, quantityAfter: 6, requestedAfter: 10, shortageAfter: 4, origin: 'Evento BomReleased · mensagem 0199', isBackfill: false, at: '2026-10-23T12:01:00Z' }],
  page: 1, pageSize: 10, total: 1,
}
const shortageRow = {
  itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', shortQty: 4, available: 0, nearestPromisedDate: '2026-12-20',
  orders: [{ productionOrderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0007', shortQty: 4, promisedDate: '2026-12-20', isBackfill: false }], purchaseRequestLink: null,
}
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

async function reservationsApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ production: { actions: ALL } as never, inventory: { actions: ALL } as never }) }),
    'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
    'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, { orderId: 'of1', canComplete: false, completeBlocker: 'Falta concluir a etapa Montagem.', canReverse: false, milestones: [], version: 7 }),
    'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, []),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/inventory/production-orders/of1/reservations/history': (r: Route) => json(r, history),
    'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, material(false)),
    'GET /api/v1/inventory/shortages': (r: Route) => json(r, page1([shortageRow])),
    'GET /api/v1/inventory/projects': (r: Route) => json(r, [{ id: 'p1', number: 'PRJ-2026-0007', name: 'Aurora — Elevador' }]),
    ...overrides,
  })
}

test.describe('Reservas e faltas (EST-02)', () => {
  test('BOM liberada com falta: "Reservando material…", aba Material, chip no cabeçalho e histórico', async ({ page }, info) => {
    let calls = 0
    await reservationsApi(page, {
      // O cabeçalho e a aba leem a mesma consulta: as duas primeiras respostas ainda estão "reservando".
      'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, material(calls++ < 2)),
    })
    await page.goto('/app/producao/os/of1?aba=material')
    await expect(page.getByRole('heading', { name: 'OS-2026-00041 — Aurora — Elevador' })).toBeVisible()
    await expect(page.getByText('Reservando material…').or(page.getByText('Falta 4', { exact: true })).first()).toBeVisible()
    // A aba se atualiza sozinha até as reservas chegarem à revisão da BOM (padrão 7).
    await expect(page.getByText('Falta 4', { exact: true })).toBeVisible({ timeout: 15_000 })
    await expect(page.getByText('Reservando material…')).toHaveCount(0)
    await expect(page.getByText('Material reservado: 2 itens, 1 em falta.')).toBeVisible()
    await expect(page.getByRole('button', { name: /Falta material: 1 item/ })).toBeVisible()
    const table = page.getByRole('table', { name: 'Material da OS' })
    await expect(table.getByRole('link', { name: 'Gerar solicitação de compra' })).toBeVisible()
    await expect(table.getByText('Calculado na implantação')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Histórico da reserva' }).or(page.locator('section[aria-labelledby="historico-reserva"]'))).toContainText('Evento BomReleased · mensagem 0199')
    if (info.project.name === 'celular') {
      // 400 px: item, reservado e falta; necessidade, baixado e local na linha expansível (padrão 8).
      await expect(table.getByRole('columnheader', { name: 'Necessidade' })).toBeHidden()
      await expect(table.getByRole('columnheader', { name: 'Reservado' })).toBeVisible()
      await table.getByRole('button', { name: /material MP-001/ }).click()
      await expect(page.getByText(/Necessidade 10 M · Baixado 0 · Local ALM 6/)).toBeVisible()
      await expectNoHorizontalScroll(page)
    } else {
      await expect(table.getByRole('columnheader', { name: 'Necessidade' })).toBeVisible()
    }

    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await page.getByRole('link', { name: 'Ver todas as faltas' }).click()
    await expect(page).toHaveURL(/\/app\/estoque\/faltas/)
  })

  test('lista Faltas: falta somada, OS afetada, prometido e o caminho da compra', async ({ page }, info) => {
    await reservationsApi(page)
    await page.goto('/app/estoque/faltas')
    await expect(page.getByRole('heading', { name: 'Estoque: Faltas' })).toBeVisible()
    const table = page.getByRole('table', { name: 'Faltas de material' })
    await expect(table.getByRole('link', { name: 'MP-001 Cabo de aço 8 mm' })).toBeVisible()
    await expect(table.getByText('Falta 4 M')).toBeVisible()
    await expect(table.getByRole('link', { name: 'OS-2026-00041' })).toBeVisible()
    if (info.project.name === 'celular') {
      await expect(table.getByRole('columnheader', { name: 'Prometido' })).toBeHidden()
      await expectNoHorizontalScroll(page)
    } else {
      await expect(table.getByRole('cell', { name: '20/12/2026' })).toBeVisible()
    }

    await table.getByRole('link', { name: 'OS-2026-00041' }).click()
    await expect(page).toHaveURL(/\/app\/producao\/os\/of1\?aba=material/)
  })

  test('vazios: sem BOM liberada e sem falta', async ({ page }) => {
    await reservationsApi(page, {
      'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, { productionOrderId: 'of1', bomReleased: false, bomRevision: null, bomReleasedAt: null, reconciling: false, orderClosed: false, totalShortage: 0, totalOverIssued: 0, lines: [] }),
      'GET /api/v1/inventory/shortages': (r: Route) => json(r, page1([])),
    })
    await page.goto('/app/producao/os/of1?aba=material')
    await expect(page.getByText('Libere a BOM para reservar o material.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Abrir BOM' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Falta material/ })).toHaveCount(0)
    await page.goto('/app/estoque/faltas')
    await expect(page.getByText('Nenhuma falta para as OS abertas.')).toBeVisible()
  })

  test('erro ao carregar o material mostra o que houve e tenta de novo', async ({ page }) => {
    let fail = true
    await reservationsApi(page, {
      'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => (fail ? json(r, { title: 'Erro', status: 500 }, 500) : json(r, material(false))),
    })
    await page.goto('/app/producao/os/of1?aba=material')
    await expect(page.getByText('Não foi possível carregar o material da OS.')).toBeVisible({ timeout: 15_000 })
    fail = false
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByText('Falta 4', { exact: true })).toBeVisible()
  })
})
