import { describe, expect, it } from 'vitest'
import { changeText, isSaleState, openFor, saleChips, trailTone } from './sales'

describe('retaguarda de vendas (PDV-07)', () => {
  it('monta os chips na ordem da tela', () => {
    expect(saleChips({ status: 'Completed', stockStatus: 'Shortfall', hasDivergence: true, offline: true }).map((c) => c.label))
      .toEqual(['Concluída', 'Estoque: falta de saldo', 'Com divergência', 'Feita sem internet'])
    expect(saleChips({ status: 'Cancelled', stockStatus: 'Reversed', hasDivergence: false, offline: false })).toEqual([{ label: 'Cancelada', tone: 'neutral' }])
    expect(saleChips({ status: 'Returned', stockStatus: 'Posted', hasDivergence: false, offline: false })[0].label).toBe('Devolvida')
  })

  it('aceita só os estados da API no filtro da URL', () => {
    expect(isSaleState('Offline')).toBe(true)
    expect(isSaleState('qualquer')).toBe(false)
  })

  it('traduz o tom da trilha e o tempo do caixa aberto', () => {
    expect(trailTone('crit')).toBe('crit')
    expect(trailTone('outro')).toBe('info')
    expect(openFor(45)).toBe('há 45 min')
    expect(openFor(125)).toBe('há 2 h 05 min')
    expect(openFor(120)).toBe('há 2 h')
  })

  it('mostra a variação com o sinal', () => {
    expect(changeText(12.5)).toBe('+12,5%')
    expect(changeText(-3)).toBe('−3%')
    expect(changeText(null)).toBeNull()
  })
})
