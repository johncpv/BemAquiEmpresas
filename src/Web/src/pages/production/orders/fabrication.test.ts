import { describe, expect, it } from 'vitest'
import { canCancel, canChangeSteps, delayLabel, routingImpact } from './fabrication'

describe('OS de fabricação (PRD-01)', () => {
  it('etapas só mudam na OS aberta sem etapa iniciada (CA 7 e 8)', () => {
    expect(canChangeSteps({ status: 'Open', started: false })).toBe(true)
    expect(canChangeSteps({ status: 'Open', started: true })).toBe(false)
    expect(canChangeSteps({ status: 'Cancelled', started: false })).toBe(false)
  })

  it('cancelar a iniciada exige quem aprova (CA 16 e 17)', () => {
    expect(canCancel({ status: 'Open', started: false }, true, false)).toBe(true)
    expect(canCancel({ status: 'Open', started: true }, true, false)).toBe(false)
    expect(canCancel({ status: 'InProgress', started: true }, false, true)).toBe(true)
    expect(canCancel({ status: 'Cancelled', started: false }, true, true)).toBe(false)
  })

  it('faixa com texto e impacto da troca de roteiro (padrões 2 e 4)', () => {
    expect(delayLabel(3)).toBe('Atrasada 3 d')
    expect(delayLabel(null)).toBeNull()
    expect(routingImpact({ steps: 3, hours: 5.5 }, { steps: 5, hours: 12 })).toBe('3 → 5 etapas · 5,50 h → 12,00 h padrão')
  })
})