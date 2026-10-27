import { describe, expect, it } from 'vitest'
import type { QuoteScheduleSimulationView } from '@/api/generated/model'
import { lateLine, positionText, promiseChip, promiseLine, simulationHeadline, validPosition } from './schedule-simulation'

const view = (over: Partial<QuoteScheduleSimulationView> = {}): QuoteScheduleSimulationView => ({
  simulated: true, queueSize: 4, queuePosition: 5, atEnd: true, today: '2026-10-30', fabricationEnd: '2026-11-05', deliveryDate: '2026-11-12', deliveryDays: 13,
  noForecastReason: null, includesInstallation: true, promisedDays: 30, promisedDate: '2026-11-29', daysBeyondPromise: 0, newlyLateCount: 0, newlyLateProjects: [],
  ...over,
})

describe('simulação de prazo do orçamento (PLN-05)', () => {
  it('monta a frase do resultado e o prazo prometido', () => {
    expect(simulationHeadline(view())).toBe('Se aprovado hoje, a fabricação termina em 05/11/2026 e a entrega fica em 12/11/2026, com o prazo de instalação.')
    expect(simulationHeadline(view({ includesInstallation: false, deliveryDate: '2026-11-05' }))).toBe('Se aprovado hoje, a fabricação termina em 05/11/2026 e a entrega fica em 05/11/2026.')
    expect(simulationHeadline(view({ fabricationEnd: null, deliveryDate: null }))).toBeNull()
    expect(promiseLine(view())).toBe('Prazo prometido no orçamento: 30 dias → 29/11/2026')
    expect(promiseLine(view({ promisedDays: null, promisedDate: null }))).toBeNull()
  })

  it('mostra se cabe no prazo ou quantos dias passa', () => {
    expect(promiseChip(view())).toEqual({ tone: 'ok', label: 'Cabe no prazo' })
    expect(promiseChip(view({ daysBeyondPromise: 1 }))).toEqual({ tone: 'crit', label: '1 dia além do prazo' })
    expect(promiseChip(view({ daysBeyondPromise: 6 }))?.label).toBe('6 dias além do prazo')
    expect(promiseChip(view({ daysBeyondPromise: null }))).toBeNull()
  })

  it('lista quem passa a atrasar fora do fim da fila', () => {
    expect(lateLine(view())).toBeNull()
    expect(lateLine(view({ atEnd: false, queuePosition: 1, newlyLateCount: 2, newlyLateProjects: null }))).toBe('Projetos que passam a atrasar: 2')
    expect(lateLine(view({ atEnd: false, newlyLateCount: 1, newlyLateProjects: [{ projectId: 'p', number: 'PRJ-2026-0001', name: 'Torre A' }] })))
      .toBe('Projetos que passam a atrasar: 1 (PRJ-2026-0001 Torre A)')
    expect(lateLine(view({ atEnd: false, queuePosition: 2 }))).toBe('Projetos que passam a atrasar: 0')
  })

  it('posição na fila', () => {
    expect(positionText(view())).toBe('No fim da fila (posição 5)')
    expect(positionText(view({ atEnd: false, queuePosition: 2 }))).toBe('Na posição 2 de 5')
    expect(validPosition('1', 4)).toBe(1)
    expect(validPosition('5', 4)).toBe(5)
    expect(validPosition('6', 4)).toBeNull()
    expect(validPosition('0', 4)).toBeNull()
    expect(validPosition('2.5', 4)).toBeNull()
  })
})
