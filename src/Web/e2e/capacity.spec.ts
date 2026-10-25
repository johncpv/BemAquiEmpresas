import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const reference = { bomId: 'b1', productionOrderId: 'of1', orderNumber: 'OS-2026-00041', revision: 1, releasedAt: '2026-10-20T12:00:00Z', productId: 'p1', productCode: 'ELEV-6', productName: 'Elevador 6 paradas', lineCount: 2, suggested: true }
const older = { ...reference, bomId: 'b0', productionOrderId: 'of0', orderNumber: 'OS-2026-00012', suggested: false }
const line = (quantity: number) => ({
  bomLineId: 'l1', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço 8 mm', unitId: 'm', unitCode: 'M', perUnit: 2, onHand: 10, reserved: 4, available: 6, possible: 3,
  required: 2 * quantity, shortage: Math.max(0, 2 * quantity - 6), openQuantity: 4, estimatedShortageCost: null, limiting: true, warning: null,
})
const guide = (quantity: number) => ({
  bomLineId: 'l2', itemId: 'i2', itemCode: 'MP-002', itemName: 'Guia T 50', unitId: 'un', unitCode: 'UN', perUnit: 1, onHand: 9, reserved: 0, available: 9, possible: 9,
  required: quantity, shortage: Math.max(0, quantity - 9), openQuantity: 0, estimatedShortageCost: null, limiting: false, warning: null,
})
const motor = {
  bomLineId: 'l3', itemId: 'i3', itemCode: 'MP-003', itemName: 'Motor antigo', unitId: 'un', unitCode: 'UN', perUnit: null, onHand: 0, reserved: 0, available: 0, possible: null,
  required: null, shortage: null, openQuantity: null, estimatedShortageCost: null, limiting: false, warning: 'Item inativo: não entra no cálculo. Reative o item em Estoque → Itens ou troque na BOM.',
}
const capacity = (quantity: number, ref = reference) => {
  const lines = [line(quantity), guide(quantity), motor]
  return {
    reference: ref, quantity, possible: 3, limitingItemId: 'i1', limitingItemName: 'Cabo de aço 8 mm', shortageCount: lines.filter((l) => (l.shortage ?? 0) > 0).length, notCalculatedCount: 1,
    openOrdersShown: true, possibleWithOpenOrders: 5, costShown: false, estimatedShortageCost: null, lines,
  }
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

async function capacityApi(page: Page, posts: string[]) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, {
      permissions: [], isOwner: true, version: '1',
      modules: allModules({ inventory: { actions: ALL } as never, engineering: { actions: ALL } as never, purchasing: { actions: ALL } as never }),
    }),
    'GET /api/v1/inventory/production-capacity/references': (r: Route) => json(r, [reference, older]),
    'GET /api/v1/inventory/production-capacity': (r: Route) => {
      const url = new URL(r.request().url())
      const quantity = Number(url.searchParams.get('quantity') ?? '1')
      return json(r, capacity(quantity, url.searchParams.get('productionOrderId') === 'of0' ? older : reference))
    },
    'GET /api/v1/purchasing/purchase-requests/defaults': (r: Route) => json(r, { neededBy: '2030-01-15', leadDays: 15, projectId: null, projectNumber: null, projectName: null, productionOrderId: null, orderNumber: null, item: null, quantity: null }),
    'POST /api/v1/purchasing/purchase-requests': (r: Route) => { posts.push(r.request().url()); return json(r, { id: 'sc9' }, 201) },
  })
}

test.describe('Capacidade de produzir (EST-06)', () => {
  test('sem produto: vazio com o que fazer', async ({ page }) => {
    await capacityApi(page, [])
    await page.goto('/app/estoque/capacidade')
    await expect(page.getByRole('heading', { name: 'Estoque: Capacidade de produzir' })).toBeVisible()
    await expect(page.getByText('Escolha um produto ou uma OS para ver quantas unidades o estoque permite.')).toBeVisible()
    await expect(page.getByRole('group', { name: 'Operação' }).getByRole('link', { name: 'Capacidade de produzir' })).toHaveAttribute('aria-current', 'page')
  })

  test('calcular, aumentar a quantidade e abrir a solicitação preenchida', async ({ page }, info) => {
    const posts: string[] = []
    await capacityApi(page, posts)
    await page.goto('/app/estoque/capacidade?produto=p1')
    const total = page.getByRole('region', { name: 'Total possível' })
    await expect(total.getByText('3 unidades')).toBeVisible()
    await expect(total.getByText('Cabo de aço 8 mm')).toBeVisible()
    await expect(total.getByText('Com os pedidos em aberto: 5')).toBeVisible()
    await expect(page.getByText('Sugerido', { exact: true })).toBeVisible()
    await expect(page.getByText(/Item inativo: não entra no cálculo/)).toBeVisible()
    await expect(total.getByText('1 item não calculado.')).toBeVisible()
    await expect(total.getByText('Nada falta para esta quantidade.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Gerar solicitação de compra' })).toHaveCount(0)

    await page.getByLabel('Quantidade desejada').fill('5')
    await page.getByLabel('Quantidade desejada').blur()
    await expect(page).toHaveURL(/qtd=5/)
    const table = page.getByRole('table', { name: 'Itens da BOM' })
    await expect(table.getByText('Falta 4 M')).toBeVisible()
    await expect(total.getByText('Falta 1 item para esta quantidade.')).toBeVisible()
    await expect(total.getByText('3 unidades'), 'o total continua o de hoje').toBeVisible()

    if (info.project.name === 'celular') {
      await expectNoHorizontalScroll(page)
      await expect(table.getByRole('columnheader', { name: 'Físico', exact: true })).toBeHidden()
    }

    const axe = await new AxeBuilder({ page }).include('main').analyze()
    expect(axe.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')).toEqual([])

    await page.getByRole('button', { name: 'Gerar solicitação de compra' }).click()
    await expect(page.getByRole('heading', { name: 'Nova solicitação de compra' })).toBeVisible()
    await expect(page.getByRole('note').filter({ hasText: /Faltas para produzir 5 × ELEV-6, BOM da OS-2026-00041/ })).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'Observação (opcional)' })).toHaveValue(/Gerada pela Capacidade de produzir/)
    await expect(page.getByRole('combobox', { name: 'Item da linha 1' })).toContainText('MP-001 — Cabo de aço 8 mm')
    await expect(page.getByRole('combobox', { name: 'Item da linha 2' })).toHaveCount(0)
    await expect(page.getByLabel('Quantidade da linha 1')).toHaveValue('4,0000')
    await expect(page.getByLabel('Estoque geral (sem projeto)')).toBeChecked()
    await expect(page.getByLabel('Estoque geral (sem projeto)')).toBeEnabled()
    expect(posts, 'nada é gravado antes de enviar').toEqual([])
  })

  test('trocar a BOM de referência por outra OS', async ({ page }) => {
    await capacityApi(page, [])
    await page.goto('/app/estoque/capacidade?produto=p1')
    await expect(page.getByText('Sugerido', { exact: true })).toBeVisible()
    await page.getByRole('combobox', { name: 'BOM de referência' }).click()
    await page.getByRole('option', { name: /OS-2026-00012/ }).click()
    await expect(page).toHaveURL(/os=of0/)
    await expect(page.getByRole('link', { name: 'OS-2026-00012' })).toBeVisible()
    await expect(page.getByText('Sugerido', { exact: true })).toHaveCount(0)
  })
})
