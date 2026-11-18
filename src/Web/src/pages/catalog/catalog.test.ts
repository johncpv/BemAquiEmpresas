import { describe, expect, it } from 'vitest'
import { combinationCount, describeAttributes, describePack, gtinError, GTIN_INVALID, isValidGtin, marginPercent, marginTone, markupPercent, parseValues } from './catalog'

describe('código de barras (CAT-01 CA 6)', () => {
  it('aceita GTIN-8, 12, 13 e 14 com o dígito verificador certo', () => {
    expect(isValidGtin('96385074')).toBe(true)
    expect(isValidGtin('036000291452')).toBe(true)
    expect(isValidGtin('7891000315507')).toBe(true)
    expect(isValidGtin('17891000315504')).toBe(true)
    expect(isValidGtin('789 1000-315507')).toBe(true)
  })

  it('recusa dígito errado, tamanho fora e letra', () => {
    expect(isValidGtin('7891000315508')).toBe(false)
    expect(isValidGtin('78910003155')).toBe(false)
    expect(isValidGtin('789100031550A')).toBe(false)
    expect(gtinError('7891000315508')).toBe(GTIN_INVALID)
    expect(gtinError('')).toBeUndefined()
  })
})

describe('painel de custo e margem (CA 11)', () => {
  it('calcula margem sobre o preço e markup sobre o custo', () => {
    expect(marginPercent(10, 6)).toBe(40)
    expect(markupPercent(10, 6)).toBe(66.67)
    expect(marginPercent(null, 6)).toBeNull()
    expect(markupPercent(10, 0)).toBeNull()
  })

  it('pinta a margem negativa de crítico e a baixa de alerta', () => {
    expect(marginTone(-5)).toBe('crit')
    expect(marginTone(8)).toBe('warn')
    expect(marginTone(35)).toBe('ok')
    expect(marginTone(null)).toBe('neutral')
  })
})

describe('grade e embalagem (CA 5 e 15)', () => {
  it('conta as combinações e lê os valores sem repetir', () => {
    expect(parseValues('Azul, Preto, azul, ,Branco')).toEqual(['Azul', 'Preto', 'Branco'])
    expect(combinationCount([{ name: 'Cor', values: ['Azul', 'Preto', 'Branco'] }, { name: 'Tamanho', values: ['P', 'M', 'G', 'GG'] }])).toBe(12)
    expect(combinationCount([{ name: '', values: ['x'] }])).toBe(0)
    expect(describeAttributes({ Cor: 'Azul', Tamanho: 'M' })).toBe('Cor: Azul · Tamanho: M')
  })

  it('descreve a embalagem', () => {
    expect(describePack(1)).toBe('Unidade')
    expect(describePack(12)).toBe('Embalagem com 12')
  })
})
