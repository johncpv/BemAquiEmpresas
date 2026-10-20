import { describe, expect, it } from 'vitest'
import { acceptanceMissing, currentStep } from './installations'

describe('OS de instalação (COM-06)', () => {
  it('etapa atual e o que falta para o aceite', () => {
    const steps = [{ name: 'Preparação', completedAt: 'x' }, { name: 'Montagem', completedAt: null }, { name: 'Entrega técnica', completedAt: null }]
    expect(currentStep(steps)).toBe(1)
    expect(acceptanceMissing(steps, [{ text: 'Área limpa', result: null }, { text: 'Testes', result: 'Done' }])).toEqual(['Etapa Montagem', 'Checklist: Área limpa'])
    expect(currentStep([{ completedAt: 'x' }])).toBe(0)
  })
})
