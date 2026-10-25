import { describe, expect, it } from 'vitest'
import type { PlanningWarning, ScheduleRunInfo } from '@/api/generated/model'
import { chunks, failedLine, forecastText, hoursText, orderEndText, runLine, warningAction } from './planning'

const run = (over: Partial<ScheduleRunInfo> = {}): ScheduleRunInfo => ({
  id: 'r1', trigger: 'Manual', status: 'Succeeded', requestedAt: '2026-10-30T17:04:00Z', startedAt: '2026-10-30T17:04:00Z', finishedAt: '2026-10-30T17:05:00Z',
  ordersCount: 12, stepsCount: 40, changedOrdersCount: 3, withoutForecastCount: 0, durationMs: 230, error: null, ...over,
})

describe('planejamento (PLN-01a)', () => {
  it('linha da execução no horário de Brasília (CA 14)', () => {
    expect(runLine(run())).toBe('Recalculado às 14:05 (manual), 12 OS em 230 ms')
    expect(runLine(run({ trigger: 'Hourly', ordersCount: 1 }))).toBe('Recalculado às 14:05 (de hora em hora), 1 OS em 230 ms')
    expect(failedLine(run({ status: 'Failed' }))).toBe('O último cálculo falhou às 14:05. As datas mostradas são do cálculo anterior.')
  })

  it('previsto da etapa e fim previsto da OS, com o motivo quando não há data (CA 16)', () => {
    expect(forecastText({ plannedStart: '2026-10-30', plannedEnd: '2026-11-03' })).toBe('Previsto 30/10/2026 → 03/11/2026')
    expect(forecastText({ plannedStart: '2026-11-05', plannedEnd: '2026-11-05' })).toBe('Previsto 05/11/2026')
    expect(forecastText({ plannedStart: null, plannedEnd: null, noForecastReason: 'Etapa Solda sem centro de trabalho' })).toBe('Sem previsão: Etapa Solda sem centro de trabalho')
    expect(orderEndText({ orderId: 'o1', plannedStart: '2026-10-30', plannedEnd: '2026-11-05', noForecastReason: null, steps: [] })).toBe('Fim previsto 05/11/2026')
    expect(orderEndText({ orderId: 'o1', plannedStart: null, plannedEnd: null, noForecastReason: 'Centro Corte sem capacidade nos próximos 2 anos', steps: [] }))
      .toBe('Sem previsão: Centro Corte sem capacidade nos próximos 2 anos')
  })

  it('horas com uma casa só na exibição', () => {
    expect(hoursText(0.6)).toBe('0,6 h')
    expect(hoursText(12)).toBe('12 h')
    expect(hoursText(1.25)).toBe('1,3 h')
  })

  it('cada aviso leva à ação que resolve', () => {
    const w = (action: PlanningWarning['action']): PlanningWarning => ({ orderId: 'o1', orderNumber: 'OS-1', reason: 'x', action })
    expect(warningAction(w('ChooseRouting'))).toMatchObject({ label: 'Escolher roteiro', params: { id: 'o1' } })
    expect(warningAction(w('SetWorkCenter'))).toMatchObject({ label: 'Informar centro', search: { aba: 'etapas' } })
    expect(warningAction(w('ReviewCalendar'))).toMatchObject({ label: 'Revisar calendário', to: '/app/producao/calendario' })
  })

  it('ids em lotes de 100', () => {
    expect(chunks(Array.from({ length: 250 }, (_, i) => i)).map((c) => c.length)).toEqual([100, 100, 50])
    expect(chunks([])).toEqual([])
  })
})
