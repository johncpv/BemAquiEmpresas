import { describe, expect, it } from 'vitest'
import { amounts, equalPercents, remaining } from './milestones'

describe('marcos do orçamento (COM-01b)', () => {
  it('valores pelo % com o último absorvendo os centavos (CA 1 e 3)', () => {
    expect(amounts(100000, [30, 40, 30])).toEqual([30000, 40000, 30000])
    expect(amounts(100, [33.3333, 33.3333, 33.3334])).toEqual([33.33, 33.33, 33.34])
  })

  it('dividir igualmente e o que falta', () => {
    expect(equalPercents(3)).toEqual([33.3333, 33.3333, 33.3334])
    expect(remaining([30, 60])).toBe('Faltam 10,00%')
    expect(remaining([60, 45])).toBe('Passou 5,00%')
    expect(remaining([50, 50])).toBeNull()
  })
})
