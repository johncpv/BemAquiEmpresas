import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const iso = (days: number) => {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
  const [y, m, d] = today.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
const br = (date: string) => `${date.slice(8, 10)}/${date.slice(5, 7)}/${date.slice(0, 4)}`
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })

const cableOption = { id: 'i1', code: 'MP-001', name: 'Cabo de aço 8 mm', type: 'RawMaterial', unitId: 'm', unitCode: 'M', units: [{ unitId: 'm', code: 'M', isStockUnit: true, toStock: 1 }, { unitId: 'rl', code: 'RL', isStockUnit: false, toStock: 100 }] }
const line = (overrides: Record<string, unknown> = {}) => ({
  id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', quantity: 4, enteredQuantity: null, enteredUnitCode: null, neededBy: iso(-3),
  status: 'Open', productionOrderId: 'of1', orderNumber: 'OS-2026-00041', bomLineId: 'b1', manuallyEdited: false, calculatedOnOnboarding: true, cancelReason: null, version: 3, ...overrides,
})
const detail = (overrides: Record<string, unknown> = {}) => ({
  id: 'sc1', number: 'SC-2026-0001', origin: 'BomShortage', status: 'Open', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', productionOrderId: 'of1',
  orderNumber: 'OS-2026-00041', notes: null, requestedBy: null, requestedByName: null, createdAt: '2026-10-23T12:00:00Z', updatedAt: '2026-10-23T12:00:00Z', cancelReason: null, cancelledAt: null,
  cancelledByName: null, nearestNeededBy: iso(-3), canEdit: true, canCancel: true, lines: [line()], version: 9, ...overrides,
})
const summary = (id: string, number: string, origin: string, neededBy: string | null, status = 'Open') => ({
  id, number, origin, status, projectId: origin === 'BomShortage' ? 'p1' : null, projectNumber: origin === 'BomShortage' ? 'PRJ-2026-0007' : null, projectName: origin === 'BomShortage' ? 'Aurora — Elevador' : null,
  productionOrderId: origin === 'BomShortage' ? 'of1' : null, orderNumber: origin === 'BomShortage' ? 'OS-2026-00041' : null, itemSummary: 'MP-001 Cabo de aço 8 mm e mais 1', lineCount: 2, openLineCount: 2,
  nearestNeededBy: neededBy, createdAt: '2026-10-23T12:00:00Z',
})

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

async function purchasingApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, {
      permissions: [], isOwner: true, version: '1',
      modules: allModules({ purchasing: { actions: ALL } as never, production: { actions: ALL } as never, inventory: { actions: ALL } as never }),
    }),
    'GET /api/v1/purchasing/purchase-requests': (r: Route) => json(r, page1([summary('sc1', 'SC-2026-0001', 'BomShortage', iso(-3)), summary('sc2', 'SC-2026-0002', 'MinStock', iso(5)), summary('sc3', 'SC-2026-0003', 'Manual', iso(30))])),
    'GET /api/v1/purchasing/purchase-requests/by-item': (r: Route) => json(r, page1([{
      itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', openQuantity: 9, nearestNeededBy: iso(-3), requestCount: 2,
      requests: [{ requestId: 'sc1', number: 'SC-2026-0001', origin: 'BomShortage', quantity: 4, neededBy: iso(-3), projectNumber: 'PRJ-2026-0007', orderNumber: 'OS-2026-00041' },
        { requestId: 'sc2', number: 'SC-2026-0002', origin: 'MinStock', quantity: 5, neededBy: iso(5), projectNumber: null, orderNumber: null }],
    }])),
    'GET /api/v1/purchasing/projects': (r: Route) => json(r, [{ id: 'p1', number: 'PRJ-2026-0007', name: 'Aurora — Elevador' }]),
    'GET /api/v1/purchasing/items': (r: Route) => json(r, [cableOption]),
    'GET /api/v1/purchasing/purchase-requests/defaults': (r: Route) => json(r, { neededBy: iso(15), leadDays: 15, projectId: null, projectNumber: null, projectName: null, productionOrderId: null, orderNumber: null, item: null, quantity: null }),
    'GET /api/v1/purchasing/purchase-requests/sc1': (r: Route) => json(r, detail()),
    'GET /api/v1/purchasing/purchase-requests/sc1/history': (r: Route) => json(r, page1([{ id: 'h1', entity: 'PurchaseRequestLine', entityId: 'l1', action: 'Created', userId: null, at: '2026-10-23T12:00:00Z', changes: '{"quantity":{"old":null,"new":4}}', origin: 'Sistema · Calculado na implantação' }])),
    ...overrides,
  })
}

test.describe('Solicitações de compra (CMP-01)', () => {
  test('falta da BOM liberada: "SC gerada" na aba Material leva à solicitação da falta', async ({ page }) => {
    const material = {
      productionOrderId: 'of1', bomReleased: true, bomRevision: 1, bomReleasedAt: '2026-10-23T12:00:00Z', reconciling: false, orderClosed: false, totalShortage: 4, totalOverIssued: 0,
      lines: [{ itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitCode: 'M', bomLineId: 'b1', required: 10, reserved: 6, issued: 0, shortage: 4, overIssued: 0, isBackfill: false,
        locations: [{ reservationId: 'r1', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', requested: 10, reserved: 6, issued: 0, shortage: 4, status: 'Active' }],
        purchaseRequestLink: '/app/compras/solicitacoes/sc1' }],
    }
    const order = {
      id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0007', projectName: 'Aurora — Elevador', partyId: 'cli1', partyName: 'Condomínio Aurora',
      promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Elevador 6 paradas', productId: 'i9', productCode: 'ELEV-6', productName: 'Elevador 6 paradas', quantity: 1, priority: 0,
      routingTemplateId: 'rt1', routingTemplateName: 'Elevador padrão', steps: [], totalStandardHours: 0, currentStepIndex: 0, started: true, delayDays: null, cancelReason: null, cancelledAt: null,
      createdAt: '2026-10-21T12:00:00Z', version: 7, qualityAttempt: 1, selfInspection: false, readyToComplete: false, openNonConformities: 0, completedAt: null, completedByName: null,
      completionBlocker: null, completionReversedAt: null, completionReversedByName: null, completionReversalReason: null,
    }
    await purchasingApi(page, {
      'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
      'GET /api/v1/production/production-orders/of1/completion': (r: Route) => json(r, { orderId: 'of1', canComplete: false, completeBlocker: null, canReverse: false, milestones: [], version: 7 }),
      'GET /api/v1/production/production-orders/of1/history': (r: Route) => json(r, page1([])),
      'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, []),
      'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
      'GET /api/v1/inventory/production-orders/of1/reservations/history': (r: Route) => json(r, page1([])),
      'GET /api/v1/inventory/production-orders/of1/reservations': (r: Route) => json(r, material),
    })
    await page.goto('/app/producao/os/of1?aba=material')
    const table = page.getByRole('table', { name: 'Material da OS' })
    await expect(table.getByText('Falta 4, SC gerada')).toBeVisible()
    await table.getByRole('link', { name: 'Ver solicitação' }).click()
    await expect(page).toHaveURL(/\/app\/compras\/solicitacoes\/sc1/)
    await expect(page.getByRole('heading', { name: 'SC-2026-0001 — Falta da BOM' })).toBeVisible({ timeout: 20_000 })
    const items = page.getByRole('list', { name: 'Itens da solicitação' })
    await expect(items.getByText('Calculado na implantação')).toBeVisible()
    await expect(items.getByText('Atrasada 3 d')).toBeVisible()
    await expect(page.getByText('Sistema · Falta da BOM OS-2026-00041', { exact: false })).toBeVisible()

    // Editar a linha a menor: o aviso de que a OS continua com falta.
    let sent: Record<string, unknown> | null = null
    await page.route('**/api/v1/purchasing/purchase-requests/sc1/lines/l1', async (r) => {
      sent = r.request().postDataJSON() as Record<string, unknown>
      await json(r, { request: detail({ lines: [line({ quantity: 3, manuallyEdited: true })] }), warning: 'A OS OS-2026-00041 continua com falta de 1.' })
    })
    await items.getByRole('button', { name: 'Editar linha' }).click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel('Quantidade (M)').fill('3')
    await dialog.getByLabel('Necessidade').fill(br(iso(2)))
    await dialog.getByRole('button', { name: 'Salvar linha' }).click()
    await expect(page.getByText('A OS OS-2026-00041 continua com falta de 1.')).toBeVisible()
    expect(sent).toMatchObject({ quantity: 3, neededBy: iso(2), version: 3 })
    await expect(items.getByText('Editada à mão')).toBeVisible()

    await page.getByRole('tab', { name: 'Histórico de alterações' }).click()
    await expect(page.getByText('Sistema · Calculado na implantação')).toBeVisible()
  })

  test('nova solicitação manual: necessidade sugerida, unidade convertida e "Solicitação enviada"', async ({ page }) => {
    let posted: Record<string, unknown> | null = null
    let key: string | null = null
    await purchasingApi(page, {
      'POST /api/v1/purchasing/purchase-requests': async (r: Route) => {
        posted = r.request().postDataJSON() as Record<string, unknown>
        key = r.request().headers()['idempotency-key'] ?? null
        await json(r, detail({ id: 'sc9', number: 'SC-2026-0009', origin: 'Manual', projectId: null, projectNumber: null, projectName: null, productionOrderId: null, orderNumber: null, requestedByName: 'Ana Souza', lines: [line({ productionOrderId: null, orderNumber: null, calculatedOnOnboarding: false, quantity: 200, enteredQuantity: 2, enteredUnitCode: 'RL', neededBy: iso(15) })] }), 201)
      },
      'GET /api/v1/purchasing/purchase-requests/sc9': (r: Route) => json(r, detail({ id: 'sc9', number: 'SC-2026-0009', origin: 'Manual', projectId: null, projectNumber: null, projectName: null, productionOrderId: null, orderNumber: null, requestedByName: 'Ana Souza', lines: [line({ productionOrderId: null, orderNumber: null, calculatedOnOnboarding: false })] })),
    })
    await page.goto('/app/compras/solicitacoes')
    await page.getByRole('link', { name: 'Nova solicitação' }).first().click()
    await expect(page.getByRole('heading', { name: 'Nova solicitação de compra' })).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Sugerido')).toBeVisible()
    await page.getByRole('combobox', { name: 'Item da linha 1' }).click()
    await page.getByRole('option', { name: /MP-001/ }).click()
    await page.getByRole('textbox', { name: 'Quantidade da linha 1' }).fill('2')
    await page.getByRole('combobox', { name: 'Unidade da linha 1' }).selectOption('rl')
    await expect(page.getByText('= 200 M')).toBeVisible()
    await page.getByLabel('Estoque geral (sem projeto)').check()
    await page.getByRole('button', { name: 'Enviar solicitação' }).click()
    await expect(page.getByText('Solicitação enviada')).toBeVisible()
    await expect(page).toHaveURL(/\/app\/compras\/solicitacoes\/sc9/)
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toBeVisible()
    expect(posted).toMatchObject({ projectId: null, lines: [{ itemId: 'i1', quantity: 2, unitId: 'rl', neededBy: iso(15) }] })
    expect(key).toBeTruthy()
  })

  test('lista a 400 px: número, itens, necessidade e situação; chips de urgência; visão Por item', async ({ page }, info) => {
    await purchasingApi(page)
    await page.goto('/app/compras/solicitacoes')
    await expect(page.getByRole('heading', { name: 'Compras: Solicitações' })).toBeVisible()
    await expect(page.getByText('Mostrando: Aguardando você')).toBeVisible()
    const table = page.getByRole('table', { name: 'Solicitações de compra' })
    await expect(table.getByRole('link', { name: 'SC-2026-0001' })).toBeVisible()
    await expect(table.getByText('Atrasada 3 d')).toBeVisible()
    await expect(table.getByText('Precisa em 5 dias')).toBeVisible()
    if (info.project.name === 'celular') {
      await expect(table.getByRole('columnheader', { name: 'Destino' })).toBeHidden()
      await expect(table.getByRole('columnheader', { name: /Necessidade/ })).toBeVisible()
      await table.getByRole('button', { name: /detalhes de SC-2026-0001/ }).click()
      await expect(page.getByText(/Falta da BOM · PRJ-2026-0007 — Aurora — Elevador · OS-2026-00041/)).toBeVisible()
      await expectNoHorizontalScroll(page)
    } else {
      await expect(table.getByRole('cell', { name: 'Estoque geral' }).first()).toBeVisible()
    }

    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await page.getByRole('button', { name: 'Por item' }).click()
    const byItem = page.getByRole('table', { name: 'Itens a comprar' })
    await expect(byItem.getByText('9 M')).toBeVisible()
    await expect(page).toHaveURL(/visao=item/)
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
  })

  test('vazio e erro', async ({ page }) => {
    let fail = true
    await purchasingApi(page, {
      'GET /api/v1/purchasing/purchase-requests': (r: Route) => (fail ? json(r, { title: 'Erro', status: 500 }, 500) : json(r, page1([]))),
    })
    await page.goto('/app/compras/solicitacoes')
    await expect(page.getByRole('button', { name: 'Tentar de novo' })).toBeVisible({ timeout: 15_000 })
    fail = false
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByText('Nenhuma solicitação aberta. As faltas da BOM e o estoque mínimo abrem solicitações sozinhos.')).toBeVisible()
  })

  test('módulo não liberado: tela padrão com o cadeado', async ({ page }) => {
    await purchasingApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ purchasing: { released: false } }) }),
    })
    await page.goto('/app/compras/solicitacoes')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })
})
