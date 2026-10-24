import { describe, expect, it } from 'vitest'
import type { StockItemContext } from '@/api/generated/model'
import { adjustmentProblems, clearQuantities, deltaOf, hasQuantities, previewRows, relocate, rowFromContext, systemQty, toLineInputs, totalsOf } from './adjustments'

const context = (change: Partial<StockItemContext> = {}): StockItemContext => ({
  itemId: 'i1', code: 'MP-001', name: 'Guia T89', unitId: 'un', unitCode: 'UN', tracksLot: false, isActive: true, type: 'RawMaterial', averageCost: 12.5, totalOnHand: 13,
  positions: [
    { locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado', onHand: 10, reserved: 4, available: 6, hasMovement: true },
    { locationId: 'exp', locationCode: 'EXP', locationName: 'Expedição', onHand: 3, reserved: 0, available: 3, hasMovement: true },
  ],
  lots: [], suggestedLotNumber: null, defaultLocationId: 'alm', manualApprovalAmount: 1000, manualCostDeviationPct: 20, openingOpen: false, ...change,
})

describe('ajuste de estoque (EST-01b)', () => {
  it('quantidade contada vira diferença sobre o saldo do sistema; diferença fica como veio (CA 2)', () => {
    const row = { ...rowFromContext(context(), [], 'alm'), quantity: '7' }
    expect(systemQty(row)).toBe(10)
    expect(deltaOf('Counted', row)).toBe(-3)
    expect(deltaOf('Delta', { ...row, quantity: '-2.5' })).toBe(-2.5)
    expect(deltaOf('Counted', { ...row, quantity: null })).toBeNull()
  })

  it('converte a unidade e mantém o sinal da diferença (R-EST-3)', () => {
    const conversions = [{ id: 'c1', fromUnitId: 'cx', fromUnitCode: 'CX', toUnitId: 'un', toUnitCode: 'UN', factor: 12 }] as never
    const row = { ...rowFromContext(context(), conversions, 'alm'), unitId: 'cx', quantity: '-1' }
    expect(row.unitOptions.map((u) => u.code)).toEqual(['UN', 'CX'])
    expect(deltaOf('Delta', row)).toBe(-12)
    expect(toLineInputs([row])).toEqual([{ itemId: 'i1', lotId: null, quantity: -1, unitId: 'cx' }])
  })

  it('a prévia recusa abaixo do reservado com a mesma mensagem do servidor (CA 4) e soma sobras e faltas', () => {
    const below = previewRows('Counted', [{ ...rowFromContext(context(), [], 'alm'), quantity: '2' }])
    expect(below[0].refusal).toBe('O saldo ficaria 2,0000 UN, abaixo do reservado (4,0000 UN) para OS.')
    const ok = previewRows('Counted', [{ ...rowFromContext(context(), [], 'alm'), quantity: '4' }])
    expect(ok[0].refusal).toBeNull()
    expect(ok[0].value).toBe(75)
    expect(totalsOf([...ok, { system: 1, delta: 2, value: 25, positionAfter: 3, refusal: null }])).toEqual({ surplus: 25, shortage: 75 })
  })

  it('o lote não fica negativo e a posição soma as linhas do item', () => {
    const lotted = context({ tracksLot: true, lots: [{ lotId: 'l1', lotNumber: 'L-1', locationId: 'alm', onHand: 6 }, { lotId: 'l2', lotNumber: 'L-2', locationId: 'alm', onHand: 4 }] })
    const a = { ...rowFromContext(lotted, [], 'alm'), lotId: 'l1', quantity: '-7' }
    expect(previewRows('Delta', [a])[0].refusal).toBe('O saldo do lote ficaria -1,0000 UN, abaixo de zero.')
    const b = { ...rowFromContext(lotted, [], 'alm'), lotId: 'l2', quantity: '1' }
    const both = previewRows('Delta', [{ ...a, quantity: '-4' }, b])
    expect(both.map((p) => p.positionAfter)).toEqual([7, 7])
    expect(both.every((p) => p.refusal === null)).toBe(true)
  })

  it('pendências antes de enviar: local, motivo, "Outro" com descrição, linha sem diferença e repetida', () => {
    const row = { ...rowFromContext(context(), [], 'alm'), quantity: '10' }
    const problems = adjustmentProblems('Counted', '', 'Other', '', [row, row])
    expect(problems).toContain('Local escolhido.')
    expect(problems).toContain('Descrição do motivo "Outro".')
    expect(problems.some((p) => p.startsWith('Diferença em MP-001'))).toBe(true)
    expect(problems).toContain('MP-001 aparece duas vezes.')
    expect(adjustmentProblems('Counted', 'alm', 'Loss', '', [{ ...row, quantity: '9' }])).toEqual([])
    expect(adjustmentProblems('Counted', 'alm', 'Loss', '', [])).toEqual(['Ao menos um item no ajuste.'])
  })

  it('trocar o local relê o saldo e trocar o modo limpa as quantidades', () => {
    const row = { ...rowFromContext(context(), [], 'alm'), quantity: '9' }
    const moved = relocate([row], 'exp')[0]
    expect([moved.onHand, moved.reserved, moved.quantity, moved.key]).toEqual([3, 0, '9', row.key])
    expect(hasQuantities([row])).toBe(true)
    expect(hasQuantities(clearQuantities([row]))).toBe(false)
  })
})
