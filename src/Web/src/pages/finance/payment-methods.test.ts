import { describe, expect, it } from 'vitest'
import type { AcquirerFeeView } from '@/api/generated/model'
import { defaultNfeCode, describeFee, minValidFrom } from './payment-methods'

const fee = (over: Partial<AcquirerFeeView>): AcquirerFeeView => ({
  id: crypto.randomUUID(), brand: 'Visa', installments: 1, feePercent: 2.5, settlementDays: 30, validFrom: '2026-11-01', validTo: null,
  inForce: true, createdAt: '2026-11-01T12:00:00Z', createdBy: null, createdByName: null, ...over,
})

describe('tabela de taxas (PAG-01)', () => {
  it('a primeira taxa da bandeira começa em qualquer data; com taxa vigente, a partir de hoje e depois do início dela', () => {
    expect(minValidFrom([], 'Visa', 1, '2026-11-16')).toBeNull()
    const fees = [fee({ validFrom: '2026-10-01', validTo: '2026-11-01', inForce: false }), fee({ validFrom: '2026-11-01' }), fee({ brand: 'Elo', validFrom: '2026-12-10' })]
    expect(minValidFrom(fees, 'Visa', 1, '2026-11-16')).toBe('2026-11-16')
    expect(minValidFrom(fees, 'Elo', 1, '2026-11-16')).toBe('2026-12-11')
    expect(minValidFrom(fees, 'Visa', 2, '2026-11-16')).toBeNull()
  })

  it('mostra taxa e prazo e sugere o tPag pelo tipo', () => {
    expect(describeFee({ feePercent: 3.4999, settlementDays: 30 })).toBe('3,4999% · D+30')
    expect(describeFee({ feePercent: 0, settlementDays: 1 })).toBe('0,00% · D+1')
    expect(defaultNfeCode('Pix')).toBe('17')
    expect(defaultNfeCode('CreditInstallments')).toBe('03')
  })
})
