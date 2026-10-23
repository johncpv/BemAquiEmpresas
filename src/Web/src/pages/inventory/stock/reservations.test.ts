import { describe, expect, it } from 'vitest'
import type { OrderMaterialLine, OrderMaterialView, StockItemContext } from '@/api/generated/model'
import { deltaText, locationsText, orderShortageSummary, shortageLabel } from './reservations'
import { previewImpact } from './stock'

const line = (over: Partial<OrderMaterialLine> = {}): OrderMaterialLine => ({
  itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço', unitCode: 'M', bomLineId: 'b1', required: 10, reserved: 6, issued: 0, shortage: 4, overIssued: 0, isBackfill: false,
  locations: [{ reservationId: 'r1', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado', requested: 10, reserved: 6, issued: 0, shortage: 4, status: 'Active' }],
  purchaseRequestLink: null, ...over,
})

const view = (lines: OrderMaterialLine[]): OrderMaterialView => ({
  productionOrderId: 'o1', bomReleased: true, bomRevision: 1, bomReleasedAt: '2026-10-22T12:00:00Z', reconciling: false, orderClosed: false,
  totalShortage: lines.reduce((s, l) => s + l.shortage, 0), totalOverIssued: 0, lines,
})

describe('reservas e faltas (EST-02)', () => {
  it('chip da falta como no protótipo', () => {
    expect(shortageLabel(4)).toBe('Falta 4')
    expect(shortageLabel(4.5, '/app/compras/solicitacoes/1')).toBe('Falta 4,5, SC gerada')
    expect(shortageLabel(0)).toBeNull()
  })

  it('cabeçalho da OS conta as linhas com falta', () => {
    expect(orderShortageSummary(view([line(), line({ itemId: 'i2', shortage: 0 })]))).toBe('Falta material: 1 item')
    expect(orderShortageSummary(view([line(), line({ itemId: 'i2' })]))).toBe('Falta material: 2 itens')
    expect(orderShortageSummary(view([line({ shortage: 0 })]))).toBeNull()
    expect(orderShortageSummary(undefined)).toBeNull()
  })

  it('locais e variação em texto curto', () => {
    expect(locationsText(line())).toBe('ALM 6')
    expect(deltaText(6)).toBe('+6')
    expect(deltaText(-3)).toBe('−3')
  })

  it('a saída avulsa respeita o disponível, não o físico (CA 13)', () => {
    const context = { positions: [{ locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado', onHand: 10, reserved: 4, available: 6, hasMovement: true }], averageCost: 5, totalOnHand: 10, manualApprovalAmount: 1000, manualCostDeviationPct: 20 } as unknown as StockItemContext
    expect(previewImpact('Issue', context, 'alm', 7, null)).toMatchObject({ insufficient: true, available: 6, reserved: 4 })
    expect(previewImpact('Issue', context, 'alm', 6, null).insufficient).toBe(false)
  })
})
