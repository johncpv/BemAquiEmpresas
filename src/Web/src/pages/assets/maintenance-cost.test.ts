import { describe, expect, it } from 'vitest'
import { costSummary, defaultPeriod, formatHours, monthLabel } from './maintenance-cost'

describe('custo de manutenção (PAT-03b)', () => {
  it('mês curto e período padrão de 12 meses', () => {
    expect(monthLabel('2026-11')).toBe('nov/26')
    expect(defaultPeriod(new Date(2026, 10, 15))).toEqual({ from: '2025-12', to: '2026-11' })
    expect(formatHours(4.5)).toBe('4,5 h')
  })

  it('resumo textual com o total, a corretiva e o mês mais caro', () => {
    const cell = (preventive: number, corrective: number) => ({ preventive, corrective, total: preventive + corrective, orders: 1 })
    const months = [{ month: '2026-10', cost: cell(100, 0) }, { month: '2026-11', cost: cell(0, 300) }]
    const text = costSummary(months, cell(100, 300))
    expect(text).toContain('75% em corretiva')
    expect(text).toContain('nov/26')
    expect(costSummary(months, cell(0, 0))).toBe('Nenhum custo de manutenção no período.')
  })
})
