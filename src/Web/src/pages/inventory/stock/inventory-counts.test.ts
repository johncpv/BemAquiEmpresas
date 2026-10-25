import { describe, expect, it } from 'vitest'
import type { InventoryCountingLine, InventoryDivergenceLine } from '@/api/generated/model'
import { closeTotals, countingOrder, lineLabel, linesLabel, parseCount, pressKey, toKeypad } from './inventory-counts'

const line = (change: Partial<InventoryCountingLine>): InventoryCountingLine => ({
  id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Polia', unitCode: 'UN', stockUnitId: 'un', units: [{ id: 'un', code: 'UN' }], tracksLot: false, lotId: null,
  lotNumber: null, countedQty: null, enteredQuantity: null, enteredUnitCode: null, countedByName: null, countedAt: null, countedByMe: false, isRecount: false, addedDuringCount: false,
  version: 1, ...change,
})

describe('inventário (EST-05)', () => {
  it('lê o número do teclado com vírgula e recusa o inválido', () => {
    expect(parseCount('12,5')).toBe(12.5)
    expect(parseCount('0')).toBe(0)
    expect(parseCount('')).toBeNull()
    expect(parseCount('1,23456')).toBeNull()
    expect(parseCount('-2')).toBeNull()
    expect(toKeypad(3.25)).toBe('3,25')
    expect(toKeypad(null)).toBe('')
  })

  it('teclado: uma vírgula, até 4 casas e sem zero à esquerda', () => {
    expect(pressKey('', ',')).toBe('0,')
    expect(pressKey('1,', ',')).toBe('1,')
    expect(pressKey('1,2345', '6')).toBe('1,2345')
    expect(pressKey('0', '7')).toBe('7')
    expect(pressKey('12', '3')).toBe('123')
  })

  it('no celular, a recontagem e o não contado vêm antes do contado', () => {
    const lines = [line({ id: 'a', sequence: 1, countedQty: 3 }), line({ id: 'b', sequence: 2 }), line({ id: 'c', sequence: 3, isRecount: true })]
    expect(countingOrder(lines).map((l) => l.id)).toEqual(['c', 'b', 'a'])
  })

  it('rótulo com lote e totais do fechamento', () => {
    expect(lineLabel({ itemCode: 'MP-1', itemName: 'Guia', lotNumber: 'L7' })).toBe('MP-1 — Guia · lote L7')
    const d = (difference: number, value: number) => ({ difference, value }) as InventoryDivergenceLine
    expect(closeTotals([d(2, 20), d(-1, 12.5), d(-3, 30)])).toEqual({ surplus: 20, shortage: 42.5, net: -22.5 })
    expect(linesLabel(1)).toBe('1 linha')
    expect(linesLabel(4)).toBe('4 linhas')
  })
})
