import { describe, expect, it } from 'vitest'
import { dayLabel, defaultRange, ENTRY_TYPES, formatTime, groupByDay, INTERNAL_CONTROL_BANNER, locationText, serverClock, serverOffset, sourceLabel, todayInSaoPaulo } from './time-clock'

describe('ponto (RH-01a)', () => {
  it('faixa de controle interno com o texto do CA 1', () => {
    expect(INTERNAL_CONTROL_BANNER).toBe('Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).')
  })

  it('os quatro tipos na ordem da sequência sugerida, com o verbo', () => {
    expect(ENTRY_TYPES.map((t) => t.value)).toEqual(['In', 'BreakStart', 'BreakEnd', 'Out'])
    expect(ENTRY_TYPES[0].verb).toBe('Registrar entrada')
  })

  it('origem: App, PIN no aparelho e Manual', () => {
    expect(sourceLabel('App')).toBe('App')
    expect(sourceLabel('Pin', 'Tablet Linha 1')).toBe('PIN no Tablet Linha 1')
    expect(sourceLabel('Manual')).toBe('Manual')
  })

  it('hora em São Paulo e o relógio do servidor pela diferença medida', () => {
    expect(formatTime('2026-10-21T11:02:00Z')).toBe('08:02')
    const offset = serverOffset('2026-10-21T11:02:00Z', Date.parse('2026-10-21T11:00:00Z'))
    expect(offset).toBe(120_000)
    expect(serverClock(offset, Date.parse('2026-10-21T11:00:05Z'))).toBe('08:02:05')
  })

  it('dia sem mudar pelo fuso, agrupado do mais recente', () => {
    expect(dayLabel('2026-10-21')).toBe('Quarta-feira, 21 de outubro')
    expect(groupByDay([{ day: '2026-10-20', id: 1 }, { day: '2026-10-21', id: 2 }, { day: '2026-10-20', id: 3 }]).map((g) => [g.day, g.items.length])).toEqual([['2026-10-21', 1], ['2026-10-20', 2]])
  })

  it('localização reduzida e "Sem localização"', () => {
    expect(locationText(-23.5505, -46.6333, 19)).toBe('-23,5505, -46,6333 (± 19 m)')
    expect(locationText(null, null, null)).toBe('Sem localização')
  })

  it('período padrão dos últimos 7 dias e hoje em São Paulo', () => {
    expect(defaultRange('2026-10-03')).toEqual({ from: '2026-09-27', to: '2026-10-03' })
    expect(todayInSaoPaulo(new Date('2026-10-22T02:00:00Z'))).toBe('2026-10-21')
  })
})
