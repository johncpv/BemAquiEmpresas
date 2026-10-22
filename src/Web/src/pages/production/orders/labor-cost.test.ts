import { describe, expect, it } from 'vitest'
import { COST_ORIGIN, laborCostMarks, recalcImpact } from './labor-cost'

const nbsp = (s: string) => s.replace(/ /g, ' ')

describe('custo de mão de obra (PRD-04)', () => {
  it('marcas Sem custo-hora e Calculado na implantação só no apontamento já com custo (CA 5 e 13)', () => {
    expect(laborCostMarks({ costMissing: true, costBackfilled: false, pending: false })).toEqual(['Sem custo-hora'])
    expect(laborCostMarks({ costMissing: false, costBackfilled: true, pending: false })).toEqual(['Calculado na implantação'])
    expect(laborCostMarks({ costMissing: true, costBackfilled: true, pending: true })).toEqual([])
  })

  it('impacto do recálculo com o total só para quem vê custo (CA 6)', () => {
    expect(nbsp(recalcImpact({ recalculableCount: 3, newTotal: 360 }))).toBe('3 apontamentos · R$ 0,00 → R$ 360,00')
    expect(recalcImpact({ recalculableCount: 1, newTotal: null })).toBe('1 apontamento')
  })

  it('origens do histórico do custo (CA 17)', () => {
    expect(Object.values(COST_ORIGIN)).toEqual(['Fechamento', 'Correção', 'Recálculo', 'Implantação'])
  })
})