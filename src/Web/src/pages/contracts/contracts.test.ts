import { describe, expect, it } from 'vitest'
import { projectStage, receivablesState } from './contracts'

describe('contratos', () => {
  it('etapa do projeto e cancelado fora da lista', () => {
    expect(projectStage('Planned')).toBe(0)
    expect(projectStage('Delivered')).toBe(4)
    expect(projectStage('Cancelled')).toBe(-1)
  })

  it('parcelas: gerando, demorando e prontas', () => {
    const created = '2026-10-20T12:00:00Z'
    const t = Date.parse(created)
    expect(receivablesState(0, created, t + 5_000)).toBe('pending')
    expect(receivablesState(0, created, t + 31_000)).toBe('slow')
    expect(receivablesState(2, created, t + 31_000)).toBe('ready')
  })
})
