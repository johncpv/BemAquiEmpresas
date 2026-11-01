import { describe, expect, it } from 'vitest'
import { addDays, daysUntil, periodTone, scheduleDraftError, vacationEnd, vacationReturn } from './vacations'

describe('férias (FLH-03)', () => {
  it('fim, retorno e dias até o início', () => {
    expect(vacationEnd('2026-06-16', 30)).toBe('2026-07-15')
    expect(vacationReturn('2026-06-16', 30)).toBe('2026-07-16')
    expect(addDays('2026-02-27', 2)).toBe('2026-03-01')
    expect(daysUntil('2026-05-01', '2026-06-01')).toBe(31)
  })

  it('tom do chip do período', () => {
    expect(periodTone('warn')).toBe('warn')
    expect(periodTone('crit')).toBe('crit')
    expect(periodTone('outro')).toBe('neutral')
  })

  it('erros locais do diálogo, antes da API', () => {
    const base = { periodId: 'p', startsOn: '2026-06-01', days: '20', soldDays: '10' }
    expect(scheduleDraftError(base, 10, 30)).toBeNull()
    expect(scheduleDraftError({ ...base, periodId: '' }, 10, 30)).toBe('Escolha o período aquisitivo.')
    expect(scheduleDraftError({ ...base, soldDays: '11' }, 10, 30)).toBe('O abono pecuniário é de até 10 dias.')
    expect(scheduleDraftError({ ...base, days: '25' }, 10, 30)).toBe('O período tem 30 dias de saldo.')
    expect(scheduleDraftError({ ...base, days: '0' }, 10, 30)).toBe('Informe os dias de férias.')
  })
})
