import { describe, expect, it } from 'vitest'
import { applyLabel, canApply } from './shortfalls'

describe('divergências de estoque', () => {
  it('aplicar a baixa só quando o disponível cobre ao menos uma venda', () => {
    const row = { quantity: 5, available: 2, sales: [{ quantity: 3 }, { quantity: 2 }] }
    expect(canApply(row)).toBe(true)
    expect(applyLabel(row)).toContain('só parte')
    expect(canApply({ ...row, available: 1 })).toBe(false)
    expect(applyLabel({ ...row, available: 5 })).toBe('O disponível cobre toda a falta.')
  })
})
