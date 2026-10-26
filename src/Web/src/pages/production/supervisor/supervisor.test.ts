import { describe, expect, it } from 'vitest'
import { ageText, blocksLabel, hoursText, kanbanSearch, MOBILE_ORDER, pendingLabel, percentText, quantityText, riskText } from './supervisor'

describe('painel do supervisor (DSH-03)', () => {
  it('"Bloqueia em {n} dias" em dias úteis, hoje quando o início já chegou (CA 5)', () => {
    expect(blocksLabel(0)).toBe('Bloqueia hoje')
    expect(blocksLabel(1)).toBe('Bloqueia em 1 dia')
    expect(blocksLabel(3)).toBe('Bloqueia em 3 dias')
    expect(blocksLabel(null)).toBeNull()
  })

  it('sem production.approve_quality a fila vira contagem (CA 4)', () => {
    expect(pendingLabel(1)).toBe('1 etapa aguarda a qualidade')
    expect(pendingLabel(4)).toBe('4 etapas aguardam a qualidade')
  })

  it('números com uma casa e tabulares (CA 6)', () => {
    expect(hoursText(1.5)).toBe('1,5 h')
    expect(hoursText(2)).toBe('2,0 h')
    expect(percentText(25)).toBe('25,0%')
    expect(percentText(null)).toBe('—')
    expect(quantityText(6)).toBe('6')
    expect(quantityText(2.5)).toBe('2,5')
    expect(ageText(0)).toBe('Hoje')
    expect(ageText(5)).toBe('5 dias')
  })

  it('os chips abrem o Kanban filtrado na mesma linha (CA 2)', () => {
    expect(kanbanSearch('late', 'Cabines')).toEqual({ atrasadas: '1', linha: 'Cabines' })
    expect(kanbanSearch('risk', undefined)).toEqual({ risco: '1' })
    expect(kanbanSearch(null, undefined)).toEqual({})
  })

  it('faixa do cartão sempre com texto', () => {
    expect(riskText({ risk: 'Late', delayDays: 3 })).toEqual({ tone: 'crit', text: 'Atrasada 3 d' })
    expect(riskText({ risk: 'AtRisk', promisedDate: '2026-11-12' })).toEqual({ tone: 'warn', text: 'Em risco, prometido 12/11/2026' })
  })

  it('a 400 px a ordem é Qualidade → Faltas → Quadro → Capacidade → Indicadores', () => {
    const order = Object.entries(MOBILE_ORDER).sort((a, b) => a[1].localeCompare(b[1])).map(([code]) => code)
    expect(order).toEqual(['quality', 'shortages', 'board', 'capacity', 'indicators'])
  })
})
