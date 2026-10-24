import { describe, expect, it } from 'vitest'
import { balanceLabel, proportionalShare, receiptTotals, toStock } from './receipts'

describe('Recebimento manual (CMP-04)', () => {
  it('frete e IPI proporcionais ao que está sendo recebido, até o que sobrou; quem completa leva o resto (CA 1)', () => {
    expect(proportionalShare(100, 1000, 650, 0, false)).toBe(65)
    expect(proportionalShare(50, 1000, 650, 0, false)).toBe(32.5)
    expect(proportionalShare(100, 1000, 650, 90, false)).toBe(10)
    expect(proportionalShare(100, 1000, 350, 65, true)).toBe(35)
    expect(proportionalShare(0, 1000, 650, 0, false)).toBe(0)
  })

  it('valor dos itens com 2 casas por linha e se completa o pedido', () => {
    expect(receiptTotals([{ poLineId: 'a', quantity: 10, unitPrice: 30, open: 10 }, { poLineId: 'b', quantity: 10, unitPrice: 35, open: 20 }])).toEqual({ subtotal: 650, completes: false })
    expect(receiptTotals([{ poLineId: 'a', quantity: 3, unitPrice: 1.005, open: 3 }])).toEqual({ subtotal: 3.02, completes: true })
  })

  it('unidade do fornecedor converte pelo fator em 4 casas', () => {
    expect(toStock(3, 12)).toBe(36)
    expect(toStock(1, 15.700005)).toBe(15.7)
    expect(toStock(5, null)).toBe(5)
  })

  it('texto do saldo', () => {
    expect(balanceLabel(6, 'UN', String)).toBe('Saldo 6 UN')
    expect(balanceLabel(0, 'UN', String)).toBe('Tudo recebido')
  })
})
