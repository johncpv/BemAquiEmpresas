import { describe, expect, it } from 'vitest'
import { receivableStatusLabel } from './cash-sessions'

describe('seção "No financeiro" da sessão de caixa (CX-03)', () => {
  it('mostra a situação do título na língua do caixa', () => {
    expect(receivableStatusLabel('Paid')).toBe('Recebido')
    expect(receivableStatusLabel('Cancelled')).toBe('Estornado')
    expect(receivableStatusLabel('Open')).toBe('Em aberto')
    expect(receivableStatusLabel('Outra')).toBe('Outra')
  })
})
