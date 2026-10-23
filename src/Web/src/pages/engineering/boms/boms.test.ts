import { describe, expect, it } from 'vitest'
import { bomSituationParam, emptyLine, lineProblems, qty, requirement, shortage } from './boms'

const units = [{ unitId: 'kg', code: 'KG', isStockUnit: true, toStock: 1 }, { unitId: 'g', code: 'G', isStockUnit: false, toStock: 0.001 }]

describe('BOM da OS (ENG-02)', () => {
  it('necessidade = quantidade por unidade × quantidade da OS × (1 + perda %), na unidade de estoque (CA 3)', () => {
    expect(requirement({ quantity: '2.5000', scrapPct: '10.00', unitId: 'kg', units }, 3)).toBe(8.25)
    expect(requirement({ quantity: '250.0000', scrapPct: '0.00', unitId: 'g', units }, 2)).toBe(0.5)
    // Arredonda em 4 casas só no fim.
    expect(requirement({ quantity: '0.3333', scrapPct: '3.00', unitId: 'kg', units }, 7)).toBe(2.4031)
    expect(requirement({ quantity: null, scrapPct: '0', unitId: 'kg', units }, 1)).toBeNull()
  })

  it('falta só quando a necessidade passa do disponível; sem saldo, nada (CA 3)', () => {
    expect(shortage(10, 4)).toBe(6)
    expect(shortage(3, 4)).toBe(0)
    expect(shortage(3, -2)).toBe(3)
    expect(shortage(3, null)).toBeNull()
  })

  it('o mesmo item não se repete e a quantidade é maior que zero (CA 2, 3)', () => {
    const a = { ...emptyLine(), itemId: 'i1', quantity: '1.0000' }
    expect(lineProblems([a, { ...a }, { ...emptyLine(), itemId: 'i2', quantity: '0' }])).toEqual({
      'lines[1].itemId': 'O item já está na linha 1.',
      'lines[2].quantity': 'Informe a quantidade por unidade do produto, maior que zero, com até 4 casas.',
    })
    expect(lineProblems([{ ...emptyLine(), quantity: '1' }])).toEqual({ 'lines[0].itemId': 'Escolha o item.' })
  })

  it('quantidade sem zeros à direita e o filtro da lista abre em "vão parar" (CA 17)', () => {
    expect(qty(12.5, 'M')).toBe('12,5 M')
    expect(qty(1000)).toBe('1.000')
    expect(qty(null)).toBe('—')
    expect(bomSituationParam(undefined)).toBe('will_stop')
    expect(bomSituationParam('liberada')).toBe('released')
  })
})
