import { describe, expect, it } from 'vitest'
import { contactText, isValidStoreCode, nextRegisterNumber, normalizeStoreCode, registerLabel } from './stores'

describe('Lojas e caixas (PDV-01)', () => {
  it('normaliza o código da loja como o servidor', () => {
    expect(normalizeStoreCode('Lojão centro')).toBe('LOJAOC')
    expect(normalizeStoreCode('lj-1')).toBe('LJ1')
    expect(isValidStoreCode('LOJA')).toBe(true)
    expect(isValidStoreCode('L')).toBe(false)
  })

  it('número do caixa com dois dígitos e o próximo livre', () => {
    expect(registerLabel(2)).toBe('02')
    expect(nextRegisterNumber([])).toBe(1)
    expect(nextRegisterNumber([1, 2, 4])).toBe(3)
  })

  it('avisa a partir de 2 h sem contato', () => {
    expect(contactText(1)).toBeNull()
    expect(contactText(5)).toBe('Sem contato há 5 h')
    expect(contactText(72)).toBe('Sem contato há 3 dias')
  })
})
