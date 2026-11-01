import { describe, expect, it } from 'vitest'
import { formatDiffPct, monthOf, STATUS, streakLabel, validCrc, visibleRows } from './parallel'

describe('conferência paralela da folha (FLH-06)', () => {
  it('diferença em % sobre a contadora, com "—" sem ela (CA 1)', () => {
    expect(formatDiffPct(null)).toBe('—')
    expect(formatDiffPct(0)).toBe('0%')
    expect(formatDiffPct(0.0039)).toBe('+0,0039%')
    expect(formatDiffPct(-12.5)).toBe('-12,5%')
  })

  it('contagem das competências seguidas e a situação (CA 5)', () => {
    expect(streakLabel(2, 3)).toBe('2 de 3 competências seguidas aceitas')
    expect(STATUS.Rejected).toEqual({ label: 'Reprovada', tone: 'crit' })
    expect(STATUS.Open.label).toBe('Em conferência')
    expect(monthOf('2026-10-01')).toBe('2026-10')
  })

  it('"Só diferenças" e as maiores primeiro', () => {
    const rows = [{ equal: true, difference: 0 }, { equal: false, difference: -0.01 }, { equal: false, difference: 50 }]
    expect(visibleRows(rows, true).map((r) => r.difference)).toEqual([50, -0.01])
    expect(visibleRows(rows, false).map((r) => r.difference)).toEqual([50, -0.01, 0])
  })

  it('CRC da contadora', () => {
    expect(validCrc('SP-123456/O')).toBe(true)
    expect(validCrc('CRC')).toBe(false)
    expect(validCrc('SP-12')).toBe(false)
  })
})
