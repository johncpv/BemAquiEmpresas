import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

/** Backend em memória da EST-04 (as regras reais estão nos testes de integração .NET). */
function traceApi() {
  const calls: { url: string; body: unknown }[] = []
  const pf = { partyId: null, name: 'J. M. R.', kind: 'PF', masked: true }
  const entry = { movementId: 'mv1', sourceType: 'GoodsReceipt', sourceId: 'rc1', number: 'REC-2026-0003', label: 'Recebimento REC-2026-0003', at: '2026-10-01T13:00:00Z', quantity: 50, reversed: false }
  const serial = {
    serialId: 's1', serialNumber: 'ELV-0001-2026-00001', status: 'Shipped', itemId: 'p1', itemCode: 'ELV-0001', itemName: 'Elevador 6 paradas', locationId: 'exp', locationName: 'Expedição', createdAt: '2026-10-20T12:00:00Z',
    productionOrderId: 'o1', productionOrderNumber: 'OS-2026-0041', productionOrderStatus: 'Completed', completedAt: '2026-10-20T12:00:00Z', orderUnits: 2, projectId: 'pr1', projectNumber: 'PRJ-2026-0007',
    projectName: 'Edifício Aurora', customer: pf, shipmentId: 'r1', shipmentNumber: 'ROM-2026-0002', shippedAt: '2026-10-22T12:00:00Z',
    sharedLotsNote: 'Lotes da OS OS-2026-0041 (2 unidades): a OS não separa o material por unidade.',
    lots: [{ itemId: 'm1', itemCode: 'CABO-8', itemName: 'Cabo de aço 8 mm', unitCode: 'M', lotId: 'l1', lotNumber: 'L-01', quantity: 12, supplierName: 'Aços Minas Ltda', expiry: '2027-03-31', expiryState: 'Ok', entries: [entry] }],
  }
  const lot = {
    lotId: 'l1', lotNumber: 'L-01', itemId: 'm1', itemCode: 'CABO-8', itemName: 'Cabo de aço 8 mm', unitCode: 'M', expiry: '2027-03-31', expiryState: 'Ok', daysToExpiry: 158, supplierPartyId: 'f1',
    supplierName: 'Aços Minas Ltda', receivedAt: '2026-10-01T13:00:00Z', entries: [entry], totalReceived: 50, balances: [{ locationId: 'alm', locationName: 'Almoxarifado principal', onHand: 30 }], onHand: 30,
    totalConsumed: 20, totalOther: 0, balanced: true, others: [],
    consumptions: [
      {
        productionOrderId: 'o1', orderNumber: 'OS-2026-0041', orderStatus: 'Completed', projectId: 'pr1', projectNumber: 'PRJ-2026-0007', projectName: 'Edifício Aurora', customer: pf, at: '2026-10-10T12:00:00Z', quantity: 12,
        serials: [{ id: 's1', itemId: 'p1', serialNumber: 'ELV-0001-2026-00001', status: 'Shipped', shipmentId: 'r1', shipmentNumber: 'ROM-2026-0002', shippedAt: '2026-10-22T12:00:00Z' }], noSerialNote: null, sharedLotsNote: null,
      },
      { productionOrderId: 'o0', orderNumber: 'OS-2025-0009', orderStatus: 'Completed', projectId: null, projectNumber: null, projectName: null, customer: null, at: '2026-09-10T12:00:00Z', quantity: 8, serials: [], noSerialNote: 'Sem série: OS concluída antes do registro de séries.', sharedLotsNote: null },
    ],
  }
  const lotRow = (overrides: Record<string, unknown> = {}) => ({
    id: 'l1', itemId: 'm1', lotNumber: 'L-01', supplierPartyId: 'f1', supplierName: 'Aços Minas Ltda', receivedAt: '2026-10-01T13:00:00Z', expiry: '2026-10-20', expiryState: 'Expired', daysToExpiry: -4, onHand: 30,
    balances: [{ locationId: 'alm', locationName: 'Almoxarifado principal', onHand: 30 }], hasOutflow: false, correctionReason: null, correctedAt: null, ...overrides,
  })
  let row = lotRow()
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/inventory/traceability': (r) => {
      const q = new URL(r.request().url()).searchParams.get('q') ?? ''
      if (q.startsWith('ELV')) return json(r, { term: q, looksLikeSerial: true, serials: [{ id: 's1', itemId: 'p1', serialNumber: 'ELV-0001-2026-00001', itemCode: 'ELV-0001', itemName: 'Elevador 6 paradas', status: 'Shipped' }], lots: [] })
      if (q.startsWith('L-')) return json(r, { term: q, looksLikeSerial: false, serials: [], lots: [
        { id: 'l1', itemId: 'm1', lotNumber: 'L-01', itemCode: 'CABO-8', itemName: 'Cabo de aço 8 mm', expiry: '2027-03-31' },
        { id: 'l2', itemId: 'm2', lotNumber: 'L-02', itemCode: 'CHAPA-3', itemName: 'Chapa 3 mm', expiry: null },
      ] })
      return json(r, { term: q, looksLikeSerial: false, serials: [], lots: [] })
    },
    'GET /api/v1/inventory/items/*/serials/*/trace': (r) => json(r, serial),
    'GET /api/v1/inventory/items/*/lots/*/trace': (r) => json(r, lot),
    'GET /api/v1/inventory/items/*/lots/*/trace/export': (r) => r.fulfill({ status: 200, contentType: 'text/csv; charset=utf-8', headers: { 'content-disposition': 'attachment; filename=rastreio-lote-L-01.csv' }, body: '﻿item;lote\r\nCABO-8;L-01\r\n' }),
    'GET /api/v1/inventory/items/m1': (r) => json(r, {
      id: 'm1', type: 'RawMaterial', code: 'CABO-8', name: 'Cabo de aço 8 mm', unitId: 'u1', unitCode: 'M', categoryId: null, categoryName: null, ncm: null, cest: null, origin: 0, minStock: 0,
      tracksLot: true, tracksSerial: false, warrantyMonths: null, averageCost: 12, isActive: true, importSource: null, createdAt: '2026-10-01T12:00:00Z',
    }),
    'GET /api/v1/inventory/units': (r) => json(r, [{ id: 'u1', code: 'M', name: 'Metro', decimals: 2, isActive: true }]),
    'GET /api/v1/inventory/categories': (r) => json(r, []),
    'GET /api/v1/inventory/warranty-defaults': (r) => json(r, []),
    'GET /api/v1/inventory/items/m1/lots': (r) => json(r, [row]),
    'PUT /api/v1/inventory/items/m1/lots/l1': (r) => {
      const body = r.request().postDataJSON() as { lotNumber: string; expiry: string | null; reason: string }
      calls.push({ url: r.request().url(), body })
      row = lotRow({ lotNumber: body.lotNumber, expiry: body.expiry, expiryState: 'Ok', daysToExpiry: 90, correctionReason: body.reason, correctedAt: '2026-10-24T12:00:00Z' })
      return json(r, row)
    },
  }
  return { api, calls }
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

async function expectAccessible(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('Estoque: rastreabilidade (EST-04)', () => {
  test('busca a série, vai ao fornecedor do lote e volta ao cliente, com a árvore acessível', async ({ page }) => {
    const { api } = traceApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/rastreabilidade')
    await expect(page.getByText('Digite uma série ou um lote para ver de onde veio e para onde foi.')).toBeVisible()
    await page.getByRole('searchbox', { name: 'Série ou lote' }).fill('ELV-0001-2026-00001')
    await page.getByRole('button', { name: 'Rastrear' }).click()

    // Um só acerto abre direto a origem da série (CA 1, propositivo).
    await expect(page.getByRole('heading', { name: 'Série ELV-0001-2026-00001' })).toBeVisible()
    const origin = page.getByRole('treegrid', { name: 'De onde veio a série ELV-0001-2026-00001' })
    await expect(origin).toContainText('OS OS-2026-0041')
    await expect(origin).toContainText('Romaneio ROM-2026-0002')
    await expect(origin).toContainText('Cliente J. M. R.')
    await expect(origin).toContainText('Lotes da OS OS-2026-0041 (2 unidades): a OS não separa o material por unidade.')
    await expect(origin.getByRole('button', { name: 'Dica: Lotes da OS' })).toBeVisible()
    await expect(origin.getByRole('button', { name: 'Dica: Iniciais do cliente' })).toBeVisible()
    await expect(origin).toContainText('Fornecedor Aços Minas Ltda')
    await expect(origin).toContainText('Recebimento REC-2026-0003')
    await expect(origin.locator('tbody tr').first()).toHaveAttribute('aria-level', '1')
    await expectAccessible(page)
    await expectNoHorizontalScroll(page)

    // Do lote consumido para o destino dele (fornecedor e conta), e de volta à série e ao cliente.
    await origin.getByRole('link', { name: 'Ver destino do lote' }).click()
    await expect(page.getByRole('heading', { name: 'Lote L-01' })).toBeVisible()
    await expect(page.getByText(/Fornecedor Aços Minas Ltda/).first()).toBeVisible()
    await expect(page.getByText('Conta fechada')).toBeVisible()
    const destination = page.getByRole('treegrid', { name: 'Para onde foi o lote L-01' })
    await expect(destination).toContainText('Sem série: OS concluída antes do registro de séries.')
    await expect(destination).toContainText('Cliente J. M. R.')
    await expectNoHorizontalScroll(page)
    await destination.getByRole('link', { name: 'Ver origem da série' }).click()
    await expect(page.getByRole('heading', { name: 'Série ELV-0001-2026-00001' })).toBeVisible()
    await expect(page.getByRole('treegrid', { name: /De onde veio/ })).toContainText('Cliente J. M. R.')
  })

  test('exporta o CSV do lote (no celular, pelo menu)', async ({ page }, info) => {
    const { api } = traceApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/rastreabilidade?item=m1&lote=l1')
    await expect(page.getByRole('heading', { name: 'Lote L-01' })).toBeVisible()
    if (info.project.name === 'celular') {
      await expect(page.getByRole('columnheader', { name: 'Quantidade' })).toBeHidden()
      await page.getByText('Mais ações').click()
    }
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Exportar CSV' }).filter({ visible: true }).click()
    expect((await download).suggestedFilename()).toBe('rastreio-lote-L-01.csv')
    await expectNoHorizontalScroll(page)
  })

  test('sem resultado diz o termo; com vários acertos mostra a lista', async ({ page }) => {
    const { api } = traceApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/rastreabilidade?q=XYZ-9')
    await expect(page.getByText("Nenhuma série ou lote com 'XYZ-9'.")).toBeVisible()
    await page.getByRole('searchbox', { name: 'Série ou lote' }).fill('L-0')
    await page.getByRole('button', { name: 'Rastrear' }).click()
    await expect(page.getByRole('region', { name: 'Lotes encontrados' })).toContainText('Lote L-02')
    await page.getByRole('button', { name: /Lote L-01/ }).click()
    await expect(page.getByRole('heading', { name: 'Lote L-01' })).toBeVisible()
    await page.getByRole('button', { name: 'Voltar à busca' }).click()
    await expect(page.getByRole('region', { name: 'Lotes encontrados' })).toBeVisible()
  })

  test('aba Lotes do item: vencido em crítico e correção com motivo', async ({ page }) => {
    const { api, calls } = traceApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/itens/m1?aba=lotes')
    await expect(page.getByRole('table', { name: 'Lotes do item' })).toContainText('L-01')
    await expect(page.getByText('Vencido').first()).toBeVisible()
    await page.getByRole('button', { name: 'Detalhes de lote L-01' }).click()
    const detail = page.locator('#l1-detalhe')
    await expect(detail).toContainText('Aços Minas Ltda')
    await detail.getByRole('button', { name: 'Corrigir' }).click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByRole('textbox', { name: 'Número do lote' }).fill('L-01A')
    await dialog.getByRole('textbox', { name: 'Motivo' }).fill('Digitado errado no recebimento')
    await dialog.getByRole('button', { name: 'Salvar correção' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Lote corrigido' }).first()).toBeVisible()
    expect(calls[0].body).toEqual({ lotNumber: 'L-01A', expiry: '2026-10-20', reason: 'Digitado errado no recebimento' })
    await expect(page.getByRole('table', { name: 'Lotes do item' })).toContainText('L-01A')
    await expectNoHorizontalScroll(page)
  })
})
