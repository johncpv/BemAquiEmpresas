import { describe, expect, it } from 'vitest'
import { itemOptionHint, kindForItemType } from './quotes'

describe('busca de item no orçamento (PRC-00)', () => {
  it('tipo da linha pelo tipo do item (CA 3)', () => {
    expect(kindForItemType('Service')).toBe('Service')
    expect(kindForItemType('Finished')).toBe('Equipment')
    expect(kindForItemType('RawMaterial')).toBe('Product')
    expect(kindForItemType('Component')).toBe('Product')
    expect(kindForItemType('Consumable')).toBe('Product')
    expect(kindForItemType('Merchandise')).toBe('Product')
  })

  it('mostra o custo médio só quando a API manda (CA 2)', () => {
    expect(itemOptionHint('Produto acabado', 'UN')).toBe('Produto acabado · UN')
    expect(itemOptionHint('Produto acabado', 'UN', null)).toBe('Produto acabado · UN')
    expect(itemOptionHint('Componente', 'M', 12.5).replace(/\s/g, ' ')).toBe('Componente · M · custo médio R$ 12,50')
  })
})
