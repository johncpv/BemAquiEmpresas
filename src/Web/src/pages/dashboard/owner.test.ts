import { describe, expect, it } from 'vitest'
import type { ProjectResultLine } from '@/api/generated/model'
import { toTree } from './results/results'
import { deliveryOf, finishedMarginHref, occupancyPoints, resultPoints, resultSummary } from './owner'

const line = (key: string, kind: string, values: number[], drillable = true): ProjectResultLine =>
  ({ key, label: key, kind, values, total: values.reduce((a, b) => a + b, 0), drillable })

const LINES = [
  line('revenue', 'Revenue', [30_000, 0, 45_500.5]),
  line('Material', 'Cost', [8_200, -700.25, 0]),
  line('Labor', 'Cost', [0, 3_150.75, 0]),
  line('costs', 'Costs', [8_200, 14_450.5, 5_530.45], false),
  line('result', 'Result', [21_800, -14_450.5, 39_970.05], false),
]
const MONTHS = [{ month: '2026-08' }, { month: '2026-09' }, { month: '2026-10' }]

describe('painel do dono, DSH-01b', () => {
  it('entrega: atrasado, em risco com os dias e no prazo (texto, nunca só cor)', () => {
    expect(deliveryOf({ risk: 'Late', delayDays: 7, slackDays: null, forecastDate: null })).toEqual({ label: 'Atrasado 7 dias', tone: 'crit' })
    expect(deliveryOf({ risk: 'AtRisk', delayDays: null, slackDays: -1, forecastDate: '2026-11-10' })).toEqual({ label: '+1 dia', tone: 'warn' })
    expect(deliveryOf({ risk: 'None', delayDays: null, slackDays: 5, forecastDate: '2026-11-10' })).toEqual({ label: 'No prazo (previsão 10/11/2026)' })
  })

  it('margem realizada leva ao Orçado × realizado com os encerrados do ano', () => {
    expect(finishedMarginHref(2026)).toEqual({ to: '/app/projetos/margens', search: { situacao: 'concluido', entregueDe: '2026-01-01', entregueAte: '2026-12-31' } })
  })

  it('ocupação por semana com a semana ISO e sem valor quando não há capacidade', () => {
    expect(occupancyPoints([
      { start: '2026-10-26', end: '2026-11-01', isoWeek: 44, committedHours: 80, capacityHours: 80, percent: 100, band: 'Full' },
      { start: '2026-11-02', end: '2026-11-08', isoWeek: 45, committedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity' },
    ])).toEqual([{ semana: 'S44 · 26/10', ocupacao: 100 }, { semana: 'S45 · 02/11', ocupacao: undefined }])
  })

  it('visão mês a mês: Receita, Custos diretos com as naturezas abaixo e = Resultado', () => {
    const tree = toTree(LINES)
    expect(tree.map((n) => n.id)).toEqual(['revenue', 'costs', 'result'])
    expect(tree[1].children?.map((n) => n.id)).toEqual(['Material', 'Labor'])
    expect(resultPoints(MONTHS, LINES)).toEqual([
      { mes: 'ago/26', receita: 30_000, custos: 8_200 }, { mes: 'set/26', receita: 0, custos: 14_450.5 }, { mes: 'out/26', receita: 45_500.5, custos: 5_530.45 },
    ])
    expect(resultSummary(MONTHS, LINES, (v) => v.toFixed(2))).toBe('Resultado do período: 47319.55. O pior mês foi setembro de 2026, com -14450.50.')
  })
})
