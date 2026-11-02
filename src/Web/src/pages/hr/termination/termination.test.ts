import { describe, expect, it } from 'vitest'
import { parseMoney, terminationBody, terminationDraftError } from './termination'

const draft = { kind: 'WithoutCause', terminationDate: '2026-10-15', noticeKind: 'Indemnified', contractEndDate: '', fgtsBalance: '', fgtsStatementDate: '' }

describe('rescisão (FLH-05)', () => {
  it('lê o saldo do FGTS com vírgula ou ponto', () => {
    expect(parseMoney('10.000,50')).toBe(10000.5)
    expect(parseMoney('10000.50')).toBe(10000.5)
    expect(parseMoney('')).toBeNull()
    expect(parseMoney('abc')).toBeNaN()
  })

  it('pede o tipo, a data, o fim da experiência e o saldo do FGTS no cálculo com multa', () => {
    expect(terminationDraftError({ ...draft, kind: '' }, { needsContractEnd: false, hasFine: true, calculate: false })).toBe('Escolha o tipo do desligamento.')
    expect(terminationDraftError({ ...draft, terminationDate: '' }, { needsContractEnd: false, hasFine: true, calculate: false })).toBe('Escolha o tipo e a data para simular.')
    expect(terminationDraftError(draft, { needsContractEnd: false, hasFine: true, calculate: false })).toBeNull()
    expect(terminationDraftError(draft, { needsContractEnd: false, hasFine: true, calculate: true })).toBe('Informe o saldo do FGTS do extrato para calcular a multa.')
    expect(terminationDraftError({ ...draft, contractEndDate: '2026-10-10' }, { needsContractEnd: true, hasFine: false, calculate: false })).toContain('fim previsto')
    expect(terminationDraftError({ ...draft, fgtsBalance: '5000' }, { needsContractEnd: false, hasFine: true, calculate: true })).toBe('Informe a data do extrato do FGTS.')
  })

  it('monta o pedido com os vazios como null', () => {
    expect(terminationBody({ ...draft, fgtsBalance: '5.000,00', fgtsStatementDate: '2026-10-01' }, false)).toEqual({
      kind: 'WithoutCause', noticeKind: 'Indemnified', terminationDate: '2026-10-15', contractEndDate: null, fgtsBalance: 5000, fgtsStatementDate: '2026-10-01',
    })
    expect(terminationBody({ ...draft, noticeKind: '' }, false).noticeKind).toBeNull()
  })
})
