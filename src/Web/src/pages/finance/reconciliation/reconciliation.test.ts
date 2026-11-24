import { describe, expect, it } from 'vitest'
import { cardDeposit, entryKindsFor, fullDate, nextPeriod, ruleKeyword, sumMismatch } from './reconciliation'

describe('conciliação (BNK-03b)', () => {
  it('confere a soma 1×N e N×1 como o servidor', () => {
    expect(sumMismatch([-300], [-100, -200])).toBeNull()
    expect(sumMismatch([150, 50], [200])).toBeNull()
    expect(sumMismatch([-300], [-100, -250])).toMatch(/^A soma das transações \(-R\$\s350,00\) não bate com a linha \(-R\$\s300,00\)\.$/)
    expect(sumMismatch([1, 1], [1, 1])).not.toBeNull()
  })

  it('na saída só oferece tarifa, IOF e juros', () => {
    expect(entryKindsFor(-10).map((k) => k.value)).toEqual(['BankFee', 'Iof', 'InterestPaid'])
    expect(entryKindsFor(10).map((k) => k.value)).toEqual(['InterestReceived', 'Yield', 'OtherIn'])
  })

  it('oferece o próximo período e a palavra da regra', () => {
    expect(nextPeriod('2026-10-31')).toEqual({ from: '2026-11-01', to: '2026-11-30' })
    expect(fullDate('2026-10-05')).toBe('05/10/2026')
    expect(ruleKeyword('TAR PACOTE SERVICOS')).toBe('TAR')
    expect(ruleKeyword('PIX TARIFA')).toBe('TARIFA')
  })
})

describe('depósito da maquininha (CX-03 CA 8)', () => {
  const items = [{ id: 'a', partyId: 'p', balance: 97 }, { id: 'b', partyId: 'p', balance: 28.8 }, { id: 'c', partyId: 'q', balance: 10 }, { id: 'd', partyId: 'p', balance: 5 }]
  it('sugere os recebíveis da mesma maquininha até cobrir o depósito', () => {
    expect(cardDeposit(items, [], 120).suggested).toEqual(['a', 'b'])
  })
  it('soma o previsto, aponta a diferença e recusa maquininhas misturadas', () => {
    expect(cardDeposit(items, ['a', 'b'], 124.8)).toMatchObject({ count: 2, expected: 125.8, difference: -1, sameParty: true })
    expect(cardDeposit(items, ['a', 'c'], 107).sameParty).toBe(false)
  })
})
