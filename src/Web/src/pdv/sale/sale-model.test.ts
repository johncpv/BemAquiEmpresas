import { describe, expect, it } from 'vitest'
import { discountStacksAboveLimit, type SaleLine } from './sale-model'

const line = (lineId: string, discountCents = 0, discountApproved = false): SaleLine => ({
  lineId, itemId: 'p1', name: 'Arroz', unit: 'UN', fractional: false, quantity: '2', unitCents: 1000, priceTableVersion: 1, code: null, note: null,
  discountCents, discountReason: discountCents > 0 ? 'Cliente fiel' : null, discountApproved,
})

/** PDV-04, revisão de segurança (achado 3): o desconto no total não empilha com o dos itens; o mesmo cálculo do servidor. */
describe('desconto acumulado da venda', () => {
  it('item no limite + total no limite passa do limite da venda', () => {
    // 10%: R$ 2,00 no item (10% de 20,00) e R$ 3,80 no total (10% de 38,00) somam 14,5% de 40,00.
    expect(discountStacksAboveLimit([line('a', 200), line('b')], 380, 10)).toBe(true)
    expect(discountStacksAboveLimit([line('a', 200), line('b')], 200, 10)).toBe(false)
  })

  it('o desconto já aprovado do item fica fora da conta', () => {
    expect(discountStacksAboveLimit([line('a', 1000, true), line('b')], 300, 10)).toBe(false)
    expect(discountStacksAboveLimit([line('a', 1000, true), line('b')], 301, 10)).toBe(true)
  })
})
