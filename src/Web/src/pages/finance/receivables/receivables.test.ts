import { describe, expect, it } from 'vitest'
import { situation } from './receivables'

describe('situação do título a receber (FIN-01 CA 6 e 7)', () => {
  const base = { status: 'Open' as const, isOverdue: false, isDueToday: false }
  it('vencido e vence hoje vêm do servidor e têm prioridade', () => {
    expect(situation({ ...base, isOverdue: true })).toEqual({ label: 'Vencido', tone: 'crit' })
    expect(situation({ ...base, status: 'PartiallyPaid', isOverdue: true }).label).toBe('Vencido')
    expect(situation({ ...base, isDueToday: true })).toEqual({ label: 'Vence hoje', tone: 'warn' })
  })
  it('as demais situações gravadas', () => {
    expect(situation(base)).toEqual({ label: 'Em aberto', tone: 'info' })
    expect(situation({ ...base, status: 'PartiallyPaid' }).label).toBe('Parcial')
    expect(situation({ ...base, status: 'Paid' })).toEqual({ label: 'Pago', tone: 'ok' })
    expect(situation({ ...base, status: 'Cancelled' }).tone).toBe('neutral')
    expect(situation({ ...base, status: 'Planned' }).label).toBe('Previsto')
  })
})
