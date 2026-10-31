import { describe, expect, it } from 'vitest'
import { entryKindsFor, fullDate, nextPeriod, ruleKeyword, sumMismatch } from './reconciliation'

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
