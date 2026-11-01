import { describe, expect, it } from 'vitest'
import { dayTone, minutes, shiftMonth, shortDay, signed, situationTone } from './timesheet'

describe('espelho de ponto (RH-01c)', () => {
  it('formata minutos em h min', () => {
    expect(minutes(528)).toBe('8 h 48 min')
    expect(minutes(120)).toBe('2 h')
    expect(minutes(45)).toBe('45 min')
    expect(minutes(0)).toBe('—')
    expect(minutes(-30)).toBe('−30 min')
  })

  it('mostra o saldo do banco com sinal', () => {
    expect(signed(200)).toBe('+3 h 20 min')
    expect(signed(-310)).toBe('−5 h 10 min')
    expect(signed(0)).toBe('0 min')
    expect(signed(null)).toBe('—')
  })

  it('anda os meses e mostra o dia curto', () => {
    expect(shiftMonth('2026-01', -1)).toBe('2025-12')
    expect(shiftMonth('2026-12', 1)).toBe('2027-01')
    expect(shortDay('2026-10-05')).toBe('seg 05/10')
  })

  it('separa a cor semântica pela situação', () => {
    expect(dayTone('Absent')).toBe('crit')
    expect(dayTone('Inconsistent')).toBe('warn')
    expect(dayTone('Holiday')).toBe('neutral')
    expect(situationTone('alert')).toBe('warn')
    expect(situationTone('critical')).toBe('crit')
  })
})
