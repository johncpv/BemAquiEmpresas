import { describe, expect, it } from 'vitest'
import { revokedText, storeOf, storesOf } from './shopfloor-devices'

describe('Aparelhos (PRD-03b, PDV-01)', () => {
  it('loja do caixa vem do rótulo; aparelho da fábrica não tem loja', () => {
    expect(storeOf({ registerLabel: 'Loja Centro · Caixa 02' })).toBe('Loja Centro')
    expect(storeOf({ registerLabel: null })).toBeNull()
    expect(storesOf([{ registerLabel: 'Loja Sul · Caixa 01' }, { registerLabel: null }, { registerLabel: 'Loja Centro · Caixa 02' }, { registerLabel: 'Loja Sul · Caixa 03' }]))
      .toEqual(['Loja Centro', 'Loja Sul'])
  })

  it('texto da revogação pelo motivo', () => {
    expect(revokedText({ revokedReason: 'manual', revokedByName: 'Paula' })).toBe('Revogado por Paula')
    expect(revokedText({ revokedReason: 'replaced', revokedByName: null })).toBe('Revogado: trocado por outro aparelho no caixa')
  })
})
