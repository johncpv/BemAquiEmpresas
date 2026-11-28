import { describe, expect, it } from 'vitest'
import { hasHourlyCost, hourlyCost } from './hr'

describe('custo-hora do cargo sem folha (EXE-RH CA 4)', () => {
  it('pelo salário de referência: salário × (1 + encargos) ÷ horas', () => {
    expect(hourlyCost(3300, 80, 220)).toBeCloseTo(27, 6)
  })

  it('sem salário nem valor ajustado, o cargo fica "Sem custo-hora"', () => {
    expect(hasHourlyCost({ hourlyCost: 0 })).toBe(false)
    expect(hasHourlyCost({ hourlyCost: 41.5 })).toBe(true)
  })
})
