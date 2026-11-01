import { describe, expect, it } from 'vitest'
import { receivablePrefill, situation } from './receivables'

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

describe('título pré-preenchido pela venda do bem (PAT-02 CA 7)', () => {
  it('lê cliente, valor, emissão e descrição da URL', () => {
    expect(receivablePrefill({ cliente: 'p1', clienteNome: 'Metal Sul', valor: '48500.75', emissao: '2026-11-01', descricao: 'Venda do bem PAT-00007 — Torno' })).toEqual({
      customer: { id: 'p1', label: 'Metal Sul', termId: null }, description: 'Venda do bem PAT-00007 — Torno', amount: '48500.75', issueDate: '2026-11-01',
    })
  })

  it('sem comprador, valor inválido ou data fora do padrão, o campo fica vazio', () => {
    expect(receivablePrefill({ valor: 'abc', emissao: '01/11/2026' })).toEqual({ customer: null, description: '', amount: null, issueDate: null })
  })
})
