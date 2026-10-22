import { describe, expect, it } from 'vitest'
import type { StockItemContext } from '@/api/generated/model'
import { approvalReason, averageAfterEntry, formatQuantity, lineTotal, parsePastedLines, previewImpact, toStockQuantity } from './stock'

const context = (over: Partial<StockItemContext> = {}): StockItemContext => ({
  itemId: 'i1', code: 'MP-01', name: 'Chapa', unitId: 'un', unitCode: 'UN', tracksLot: false, isActive: true, type: 'RawMaterial', averageCost: 50, totalOnHand: 25,
  positions: [{ locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado', onHand: 20, reserved: 0, available: 20, hasMovement: true }, { locationId: 'exp', locationCode: 'EXP', locationName: 'Expedição', onHand: 5, reserved: 0, available: 5, hasMovement: true }],
  lots: [], suggestedLotNumber: null, defaultLocationId: 'alm', manualApprovalAmount: 1000, manualCostDeviationPct: 20, openingOpen: true, ...over,
})

describe('prévia do motor de estoque (EST-01a)', () => {
  it('custo médio ponderado e físico zerado (CA 18 e 19)', () => {
    expect(averageAfterEntry(10, 5, 10, 7)).toBe(6)
    expect(averageAfterEntry(0, 5, 3, 9.5)).toBe(9.5)
    expect(averageAfterEntry(3, 1, 6, 2)).toBe(1.666667)
  })

  it('total da linha em 2 casas, arredondando para cima no meio', () => {
    expect(lineTotal(3, 0.335)).toBe(1.01)
    expect(lineTotal(-3, 0.335)).toBe(-1.01)
  })

  it('mesma regra de aprovação da D7 (CA 9)', () => {
    expect(approvalReason('Issue', 1000, null, 50, 1000, 20)).toBeNull()
    expect(approvalReason('Issue', 1000.01, null, 50, 1000, 20)).toBe('Acima de R$ 1.000,00')
    expect(approvalReason('Receipt', 70, 70, 50, 1000, 20)).toBe('Custo 40% acima do médio')
    expect(approvalReason('Receipt', 7.5, 7.5, 10, 1000, 20)).toBe('Custo 25% abaixo do médio')
    expect(approvalReason('Receipt', 100, 50, 0, 1000, 20)).toBeNull()
    expect(approvalReason('Transfer', 99999, null, 50, 1000, 20)).toBeNull()
  })

  it('impacto antes → depois do local e do médio global, com saldo insuficiente', () => {
    const entry = previewImpact('Receipt', context(), 'alm', 25, 62)
    expect(entry).toMatchObject({ onHandBefore: 20, onHandAfter: 45, averageBefore: 50, averageAfter: 56, value: 1550, insufficient: false, approval: 'Acima de R$ 1.000,00 · Custo 24% acima do médio' })
    const exit = previewImpact('Issue', context(), 'exp', 6, null)
    expect(exit).toMatchObject({ onHandBefore: 5, onHandAfter: -1, averageAfter: 50, insufficient: true, value: 300 })
    const transfer = previewImpact('Transfer', context(), 'alm', 4, null, 'exp')
    expect(transfer.target).toEqual({ before: 5, after: 9 })
    expect(transfer.averageAfter).toBe(50)
    expect(transfer.approval).toBeNull()
  })

  it('conversão na borda nas duas direções e nunca fator 1 sem cadastro', () => {
    const conversions = [{ id: 'c1', itemId: 'i1', fromUnitId: 'cx', fromUnitCode: 'CX', toUnitId: 'un', toUnitCode: 'UN', factor: 12 }]
    expect(toStockQuantity(2, 'cx', 'un', conversions)).toBe(24)
    expect(toStockQuantity(2, 'un', 'un', conversions)).toBe(2)
    expect(toStockQuantity(30, 'un', 'cx', conversions)).toBe(2.5)
    expect(toStockQuantity(2, 'kg', 'un', conversions)).toBeNull()
  })

  it('linhas coladas do Excel com tabulação ou ponto e vírgula', () => {
    expect(parsePastedLines('MP-01\t10\t25,50\nMP-02;4;1.234,5;L-01\n\n')).toEqual([
      { code: 'MP-01', quantity: '10', unitCost: '25.50', lotNumber: '' },
      { code: 'MP-02', quantity: '4', unitCost: '1234.5', lotNumber: 'L-01' },
    ])
  })

  it('quantidade com sinal em pt-BR', () => {
    expect(formatQuantity(-3, true)).toBe('−3')
    expect(formatQuantity(12.5, true)).toBe('+12,5')
    expect(formatQuantity(1234.5678)).toBe('1.234,5678')
  })
})
