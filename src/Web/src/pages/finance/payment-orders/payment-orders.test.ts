import { describe, expect, it } from 'vitest'
import { destination, itemTotal } from './payment-orders'

describe('ordem de pagamento (FIN-03a)', () => {
  it('total do item: principal + juros + multa − desconto, em centavos (CA 15)', () => {
    expect(itemTotal(600, 10, 0.1, 0.2)).toBe(609.9)
    expect(itemTotal(0.1, 0.2, 0, 0)).toBe(0.3)
  })

  it('para onde vai o dinheiro numa linha', () => {
    expect(destination({ kind: 'Pix', pixKey: '529*****725', holderName: 'Fulano' })).toBe('Pix 529*****725 · Fulano')
    expect(destination({ kind: 'Ted', bankCode: '077', agency: '0001', account: '123-4', holderName: 'Gama' })).toBe('TED banco 077 ag. 0001 conta 123-4 · Gama')
  })
})
