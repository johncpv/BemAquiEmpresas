import { describe, expect, it } from 'vitest'
import type { PlanningQueueChangeView, PlanningQueuePreviewRow } from '@/api/generated/model'
import { changeMoves, moveItem, movedAnnouncement, previewDates, queueChip, sameOrder, shortDate, slackShort, worsenedLine } from './queue'

const preview = (over: Partial<PlanningQueuePreviewRow>): PlanningQueuePreviewRow => ({
  projectId: 'p1', positionBefore: 1, positionAfter: 2, promisedDate: '2026-12-05', forecastBefore: '2026-12-05', forecastAfter: '2026-12-09', slackBefore: 0, slackAfter: -4,
  riskBefore: 'None', riskAfter: 'AtRisk', worsened: true, daysLate: 4, ...over,
})

describe('fila de prioridade (PLN-02)', () => {
  it('move um projeto de posição sem perder nem repetir ninguém', () => {
    expect(moveItem(['a', 'b', 'c', 'd'], 3, 0)).toEqual(['d', 'a', 'b', 'c'])
    expect(moveItem(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a'])
    expect(moveItem(['a', 'b'], 0, 5)).toEqual(['a', 'b'])
    expect(moveItem(['a', 'b'], -1, 0)).toEqual(['a', 'b'])
    expect(sameOrder(['a', 'b'], ['a', 'b'])).toBe(true)
    expect(sameOrder(['a', 'b'], ['b', 'a'])).toBe(false)
  })

  it('anuncia a posição nova para o leitor de tela', () => {
    expect(movedAnnouncement('PRJ-2026-0003', 1)).toBe('Projeto PRJ-2026-0003 movido para a posição 1')
  })

  it('chip do prazo pela função única (CA 1)', () => {
    expect(queueChip({ risk: 'Late', hasForecast: true })).toEqual({ tone: 'crit', label: 'Atrasado' })
    expect(queueChip({ risk: 'AtRisk', hasForecast: true })).toEqual({ tone: 'warn', label: 'Em risco' })
    expect(queueChip({ risk: 'None', hasForecast: false })).toEqual({ tone: 'neutral', label: 'Sem previsão' })
    expect(queueChip({ risk: 'None', hasForecast: true })).toEqual({ tone: 'ok', label: 'No prazo' })
  })

  it('prévia em duas linhas: "05/12 → 09/12" e folga "−4 dias" (CA 4)', () => {
    expect(shortDate('2026-12-05')).toBe('05/12')
    expect(shortDate(null)).toBe('—')
    expect(previewDates(preview({}))).toBe('05/12 → 09/12')
    expect(previewDates(preview({ forecastAfter: '2026-12-05' }))).toBe('05/12')
    expect(slackShort(-7)).toBe('−7 dias')
    expect(slackShort(3)).toBe('+3 dias')
    expect(slackShort(1)).toBe('+1 dia')
    expect(slackShort(0)).toBe('0 dia')
    expect(slackShort(null)).toBe('—')
  })

  it('lista quem passa a atrasar no diálogo (CA 5)', () => {
    const names: Record<string, string> = { p1: 'PRJ-1', p2: 'PRJ-2', p3: 'PRJ-3' }
    const rows = [preview({ projectId: 'p1', daysLate: 7 }), preview({ projectId: 'p2', worsened: false }), preview({ projectId: 'p3', daysLate: 1 })]
    expect(worsenedLine(rows, (id) => names[id])).toBe('Estes projetos passam a atrasar: PRJ-1 (7 dias depois do prometido), PRJ-3 (1 dia depois do prometido).')
    expect(worsenedLine([preview({ worsened: false })], (id) => names[id])).toBeNull()
  })

  it('histórico com as posições antes → depois (CA 8)', () => {
    const change: PlanningQueueChangeView = {
      id: 'c1', at: '2026-10-30T17:00:00Z', who: 'Ana', kind: 'Manual', reason: 'Ordem alterada',
      moves: [{ projectId: 'p1', projectNumber: 'PRJ-1', from: 3, to: 1 }, { projectId: 'p2', projectNumber: 'PRJ-2', from: null, to: 2 }, { projectId: 'p3', projectNumber: 'PRJ-3', from: 4, to: null }],
    }
    expect(changeMoves(change)).toBe('PRJ-1: 3 → 1; PRJ-2: entrou na 2; PRJ-3: saiu da 4')
    expect(changeMoves(change, 1)).toBe('PRJ-1: 3 → 1; e mais 2')
  })
})
