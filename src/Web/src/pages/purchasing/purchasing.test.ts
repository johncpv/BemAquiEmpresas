import { describe, expect, it } from 'vitest'
import { daysUntil, destination, rowAccent, todayIso, urgency } from './purchasing'

describe('solicitações de compra (CMP-01)', () => {
  const today = '2026-10-23'

  it('dias até a necessidade', () => {
    expect(daysUntil('2026-10-30', today)).toBe(7)
    expect(daysUntil('2026-10-20', today)).toBe(-3)
    expect(daysUntil('2026-11-01', today)).toBe(9)
  })

  it('chip "Precisa em {n} dias" até 7 dias e "Atrasada {n} d" depois da data (CA 12)', () => {
    expect(urgency('2026-10-20', today)).toEqual({ tone: 'crit', label: 'Atrasada 3 d' })
    expect(urgency('2026-10-23', today)).toEqual({ tone: 'warn', label: 'Precisa hoje' })
    expect(urgency('2026-10-24', today)).toEqual({ tone: 'warn', label: 'Precisa em 1 dia' })
    expect(urgency('2026-10-30', today)).toEqual({ tone: 'warn', label: 'Precisa em 7 dias' })
    expect(urgency('2026-10-31', today)).toBeNull()
    expect(urgency(null, today)).toBeNull()
  })

  it('faixa lateral acompanha o chip', () => {
    expect(rowAccent('2026-10-20', today)).toContain('border-l-crit')
    expect(rowAccent('2026-10-25', today)).toContain('border-l-warn')
    expect(rowAccent('2026-12-01', today)).toBeUndefined()
  })

  it('destino: projeto ou Estoque geral', () => {
    expect(destination('PRJ-2026-0001', 'Edifício Aurora')).toBe('PRJ-2026-0001 — Edifício Aurora')
    expect(destination(null)).toBe('Estoque geral')
  })

  it('hoje em São Paulo', () => {
    expect(todayIso(new Date('2026-10-24T02:00:00Z'))).toBe('2026-10-23')
  })
})
