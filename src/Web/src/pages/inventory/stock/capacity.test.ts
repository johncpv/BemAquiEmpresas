import { describe, expect, it } from 'vitest'
import type { CapacityLine, CapacityReference, ProductionCapacityView } from '@/api/generated/model'
import { parseQuantity, productOptions, purchasePrefill, referenceLabel, shortageSummary, unitsText } from './capacity'

const reference = (over: Partial<CapacityReference> = {}): CapacityReference => ({
  bomId: 'b1', productionOrderId: 'of1', orderNumber: 'OS-2026-00041', revision: 1, releasedAt: '2026-10-20T12:00:00Z', productId: 'p1', productCode: 'ELEV-6',
  productName: 'Elevador 6 paradas', lineCount: 2, suggested: true, ...over,
})
const line = (over: Partial<CapacityLine> = {}): CapacityLine => ({
  bomLineId: 'l1', itemId: 'i1', itemCode: 'MP-001', itemName: 'Cabo de aço', unitId: 'un', unitCode: 'M', perUnit: 2, onHand: 10, reserved: 4, available: 6, possible: 3,
  required: 10, shortage: 4, openQuantity: null, estimatedShortageCost: null, limiting: true, warning: null, ...over,
})
const view = (lines: CapacityLine[]): ProductionCapacityView => ({
  reference: reference(), quantity: 5, possible: 3, limitingItemId: 'i1', limitingItemName: 'Cabo de aço', shortageCount: lines.filter((l) => (l.shortage ?? 0) > 0).length,
  notCalculatedCount: 0, openOrdersShown: false, possibleWithOpenOrders: null, costShown: false, estimatedShortageCost: null, lines,
})

describe('capacidade de produzir (EST-06)', () => {
  it('produtos distintos, na ordem da BOM mais nova', () => {
    const options = productOptions([reference(), reference({ productionOrderId: 'of2', suggested: false }), reference({ productId: 'p2', productCode: 'PORTA', productName: 'Porta' }), reference({ productId: null, productCode: null })])
    expect(options).toEqual([{ id: 'p1', label: 'ELEV-6 — Elevador 6 paradas' }, { id: 'p2', label: 'PORTA — Porta' }])
    expect(referenceLabel(reference({ productCode: null }))).toBe('OS OS-2026-00041')
  })

  it('a solicitação leva só as faltas, na unidade de estoque, e diz de onde veio', () => {
    const prefill = purchasePrefill(view([line(), line({ itemId: 'i2', itemCode: 'MP-002', shortage: 0 }), line({ itemId: 'i3', shortage: null, warning: 'Item inativo' })]))
    expect(prefill.lines).toEqual([{ itemId: 'i1', code: 'MP-001', name: 'Cabo de aço', unitId: 'un', unitCode: 'M', quantity: 4 }])
    expect(prefill.source).toBe('Faltas para produzir 5 × ELEV-6, BOM da OS-2026-00041 (rev. 1)')
    expect(prefill.leftOut).toBe(0)
  })

  it('mais de 50 faltas: as primeiras 50 e o resto avisado', () => {
    const lines = Array.from({ length: 53 }, (_, i) => line({ itemId: `i${i}`, bomLineId: `l${i}` }))
    const prefill = purchasePrefill(view(lines))
    expect(prefill.lines).toHaveLength(50)
    expect(prefill.leftOut).toBe(3)
  })

  it('textos e quantidade da URL', () => {
    expect(unitsText(1)).toBe('1 unidade')
    expect(unitsText(3)).toBe('3 unidades')
    expect(shortageSummary(0)).toBe('Nada falta para esta quantidade.')
    expect(shortageSummary(2)).toBe('Faltam 2 itens para esta quantidade.')
    expect(parseQuantity(undefined)).toBe(1)
    expect(parseQuantity('5')).toBe(5)
    expect(parseQuantity(2.5)).toBe(2.5)
    expect(parseQuantity('0')).toBe(1)
    expect(parseQuantity('abc')).toBe(1)
  })
})
