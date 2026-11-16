import { describe, expect, it } from 'vitest'
import { budgetChanges, budgetDraft, budgetedMarginPct, budgetLocked, budgetTotal } from './budget'

const lines = [{ costType: 'Material' as const, budgeted: 50000 }, { costType: 'Labor' as const, budgeted: 20000.55 }]

describe('editar custo orçado (COM-09)', () => {
  it('o diálogo abre com as seis naturezas e só as alteradas vão para a API', () => {
    const draft = budgetDraft(lines)
    expect(draft).toEqual({ Material: '50000.00', Labor: '20000.55', Installation: '0.00', Freight: '0.00', Tax: '0.00', Other: '0.00' })
    expect(budgetChanges(lines, draft)).toEqual([])
    expect(budgetChanges(lines, { ...draft, Material: '45000.00', Freight: '1200.5', Tax: null })).toEqual([{ costType: 'Material', amount: 45000 }, { costType: 'Freight', amount: 1200.5 }])
    expect(budgetTotal({ ...draft, Material: '45000.00' })).toBeCloseTo(65000.55, 2)
  })

  it('margem orçada do impacto: sem custo orçado ou sem receita não existe', () => {
    expect(budgetedMarginPct(100000, 70000)).toBeCloseTo(30, 4)
    expect(budgetedMarginPct(100000, 0)).toBeNull()
    expect(budgetedMarginPct(0, 70000)).toBeNull()
  })

  it('projeto encerrado ou cancelado não é editado', () => {
    expect([budgetLocked('Closed'), budgetLocked('Cancelled'), budgetLocked('Delivered'), budgetLocked('InProgress')]).toEqual([true, true, false, false])
  })
})
