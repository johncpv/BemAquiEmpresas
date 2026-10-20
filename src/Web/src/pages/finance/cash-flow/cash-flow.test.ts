import { describe, expect, it } from 'vitest'
import { chartPoints, chartReferences, periodLabel } from './cash-flow'

const p = (start: string, end: string, balance: number, projected: boolean, containsToday = false) =>
  ({ start, end, inflow: 0, outflow: 0, balance, projected, containsToday, overdueInflow: 0, overdueOutflow: 0 })

describe('fluxo de caixa (FIN-05)', () => {
  it('rótulo do período por visão', () => {
    expect(periodLabel({ start: '2026-10-19', end: '2026-10-25' }, 'weekly')).toBe('19/10 a 25/10')
    expect(periodLabel({ start: '2026-10-19', end: '2026-10-31' }, 'monthly')).toBe('out/2026')
    expect(periodLabel({ start: '2026-10-19', end: '2026-10-19' }, 'daily')).toBe('19/10/2026')
  })

  it('hoje entra no realizado e no projetado; marca hoje e o menor saldo', () => {
    const periods = [p('2026-10-19', '2026-10-19', 10, false), p('2026-10-20', '2026-10-20', 20, true, true), p('2026-10-21', '2026-10-21', -5, true)]
    expect(chartPoints(periods, 'daily').map((x) => [x.realizado, x.projetado])).toEqual([[10, undefined], [20, 20], [undefined, -5]])
    expect(chartReferences({ periods, view: 'daily', today: '2026-10-20', lowest: { date: '2026-10-21', balance: -5 } })).toEqual([
      { x: '20/10/2026', label: 'Hoje' }, { x: '21/10/2026', label: 'Menor saldo' },
    ])
  })
})
