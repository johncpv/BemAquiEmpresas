import { describe, expect, it } from 'vitest'
import { delta, diffItems } from './compare'

const item = (sequence: number, quantity: number, unitPrice: number) => ({ id: `i${sequence}`, sequence, kind: 'Equipment' as const, itemId: null, itemName: null, description: `Item ${sequence}`, quantity, unitPrice, priceOverridden: false, total: quantity * unitPrice })

describe('comparar revisões (COM-02 CA 9)', () => {
  it('marca incluídos, removidos e alterados', () => {
    const d = diffItems([item(1, 1, 100), item(2, 1, 50)], [item(1, 2, 100), item(3, 1, 10)])
    expect(d.map((x) => x.change)).toEqual(['Alterado', 'Removido', 'Incluído'])
  })

  it('diferença em valor e em %', () => {
    expect(delta(9000, 9500)).toEqual({ diff: 500, pct: 5.56 })
    expect(delta(0, 10)).toEqual({ diff: 10, pct: null })
  })
})
