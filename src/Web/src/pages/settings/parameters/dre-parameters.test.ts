import { describe, expect, it } from 'vitest'
import { DRE_FIELDS, dreChanged, PENDING_LABEL } from './dre-parameters'

describe('contas do DRE em Configurações (FIN-08 CA 16)', () => {
  it('as cinco chaves do parâmetro e o selo da contadora', () => {
    expect(DRE_FIELDS.map((f) => f.field)).toEqual(['interestIncomeAccountId', 'interestExpenseAccountId', 'discountGrantedAccountId', 'discountReceivedAccountId', 'downPaymentAsAdvance'])
    expect(PENDING_LABEL).toBe('A confirmar com a contadora')
  })

  it('só habilita salvar quando algum valor mudou', () => {
    const saved = Object.fromEntries(DRE_FIELDS.map((f) => [f.key, f.field === 'downPaymentAsAdvance' ? true : 'a']))
    expect(dreChanged(saved, { ...saved })).toBe(false)
    expect(dreChanged(saved, { ...saved, 'finance.dre_down_payment_as_advance': false })).toBe(true)
  })
})
