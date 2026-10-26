import { describe, expect, it } from 'vitest'
import { addDays, axis, barBox, dayNumber, daysBetween, fromDayNumber, isoWeek, isoWeekday, ticks, xOf } from './gantt-scale'

describe('escala do Gantt (PLN-03 CA 6, ADR 0033)', () => {
  it('converte datas de negócio sem mudar o dia', () => {
    expect(fromDayNumber(dayNumber('2026-10-30'))).toBe('2026-10-30')
    expect(addDays('2026-10-30', 3)).toBe('2026-11-02')
    expect(daysBetween('2026-10-23', '2027-01-22')).toBe(91)
    expect(isoWeekday('2026-11-02')).toBe(1)
    expect(isoWeekday('2026-11-01')).toBe(7)
    expect(isoWeek('2026-11-02')).toBe(45)
    expect(isoWeek('2027-01-01')).toBe(53)
  })

  it('posiciona as barras em pixels pela escala (fim inclusive)', () => {
    const week = axis('2026-10-23', '2027-01-22', 'semana')
    expect(week.days).toBe(92)
    expect(week.width).toBe(92 * 12)
    expect(xOf(week, '2026-10-30')).toBe(7 * 12)
    expect(barBox(week, '2026-11-02', '2026-11-04')).toEqual({ x: 10 * 12, width: 3 * 12, clippedStart: false, clippedEnd: false })
    const day = axis('2026-11-01', '2026-11-30', 'dia')
    expect(barBox(day, '2026-11-03', '2026-11-03')).toMatchObject({ x: 64, width: 32 })
    expect(axis('2026-11-01', '2026-11-30', 'mes').dayWidth).toBe(4)
  })

  it('corta a barra nas pontas do período e some fora dele', () => {
    const a = axis('2026-11-01', '2026-11-30', 'dia')
    expect(barBox(a, '2026-10-20', '2026-11-02')).toMatchObject({ x: 0, width: 64, clippedStart: true, clippedEnd: false })
    expect(barBox(a, '2026-11-29', '2026-12-10')).toMatchObject({ x: 28 * 32, width: 64, clippedEnd: true })
    expect(barBox(a, '2026-10-01', '2026-10-31')).toBeNull()
    expect(barBox(a, '2026-12-01', '2026-12-02')).toBeNull()
  })

  it('o eixo muda com a escala: dias, semanas ISO ou meses', () => {
    const from = '2026-10-26'
    const to = '2026-12-06'
    expect(ticks(axis(from, to, 'dia'))).toHaveLength(42)
    const weeks = ticks(axis(from, to, 'semana'))
    expect(weeks.map((t) => t.label).slice(0, 2)).toEqual(['S44 · 26/10', 'S45 · 02/11'])
    expect(weeks).toHaveLength(6)
    expect(ticks(axis(from, '2027-02-10', 'mes')).map((t) => t.label)).toEqual(['nov/26', 'dez/26', 'jan/27', 'fev/27'])
  })
})
