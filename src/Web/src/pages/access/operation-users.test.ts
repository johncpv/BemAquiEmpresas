import { describe, expect, it } from 'vitest'
import { isObviousPin, OPERATION_EMAIL_TEXT, pinProblem, userContact } from './operation-users'

describe('usuário de operação (PDV-01 CA 12)', () => {
  it('recusa PIN óbvio como o servidor', () => {
    expect(['000000', '123456', '890123', '987654', '121212', '123123'].every(isObviousPin)).toBe(true)
    expect(isObviousPin('482915')).toBe(false)
  })

  it('confere formato e confirmação', () => {
    expect(pinProblem('12345', '12345')).toBe('O PIN tem 6 números.')
    expect(pinProblem('482915', '482916')).toBe('Os dois PINs não são iguais. Digite de novo.')
    expect(pinProblem('482915', '482915')).toBeNull()
  })

  it('mostra que o usuário de operação não tem e-mail', () => {
    expect(userContact({ email: '', isOperationUser: true })).toBe(OPERATION_EMAIL_TEXT)
    expect(userContact({ email: 'ana@loja.com', isOperationUser: false })).toBe('ana@loja.com')
  })
})
