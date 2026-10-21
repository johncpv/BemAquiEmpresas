import { describe, expect, it } from 'vitest'
import { addDays, barWidth, pctLabel } from './funnel'

describe('funil comercial', () => {
  it('mostra traço sem decididos e uma casa decimal', () => {
    expect(pctLabel(null)).toBe('—')
    expect(pctLabel(undefined)).toBe('—')
    expect(pctLabel(33.3)).toBe('33,3%')
    expect(pctLabel(0)).toBe('0,0%')
  })

  it('barras proporcionais ao maior, com mínimo visível', () => {
    expect(barWidth(0, 10)).toBe(0)
    expect(barWidth(10, 10)).toBe(100)
    expect(barWidth(1, 1000)).toBe(2)
    expect(barWidth(5, 0)).toBe(0)
  })

  it('soma dias em data ISO', () => {
    expect(addDays('2026-10-21', -365)).toBe('2025-10-21')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
  })
})
