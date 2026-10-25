import { describe, expect, it } from 'vitest'
import type { GanttCenterDay, GanttOrder, GanttProject, GanttProjectsView, GanttStep } from '@/api/generated/model'
import {
  centerDay, centerRows, centersSummary, COLLAPSE_ABOVE, defaultOpen, ganttFiltersToSearch, orderBar, orderOfBar, parseGanttFilters, periodRange, projectBar, projectRows, simpleBar,
  slackChip, slackText, stepBar, summaryLine,
} from './gantt'

const step = (over: Partial<GanttStep>): GanttStep => ({
  stepId: 's1', sequence: 1, name: 'Corte', code: 'C', workCenterId: 'w1', workCenterName: 'Corte', state: 'Planned', standardHours: 6, remainingHours: 6, progress: 0,
  plannedStart: '2026-11-03', plannedEnd: '2026-11-04', actualStart: null, actualEnd: null, noForecastReason: null, drivesDelivery: false, ...over,
})

const order = (over: Partial<GanttOrder> = {}): GanttOrder => ({ orderId: 'o1', number: 'OS-2026-0001', kind: 'Fabrication', status: 'Open', start: '2026-11-03', end: '2026-11-04', steps: [step({})], ...over })

const project = (over: Partial<GanttProject> = {}): GanttProject => ({
  position: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', partyId: 'c1', partyName: 'Construtora Alfa', start: '2026-11-03', forecastDate: '2026-11-10',
  promisedDate: '2026-11-13', slackDays: 3, risk: 'None', daysLate: null, noForecastReason: null, orders: [order()], ...over,
})

const view = (projects: GanttProject[]): GanttProjectsView => ({
  today: '2026-10-30', from: '2026-10-23', to: '2027-01-22', planComputedAt: '2026-10-30T17:05:00Z', projects, projectOptions: [], partyOptions: [], lines: [], centers: [],
})

describe('cronograma (PLN-03)', () => {
  it('CA 7: o resumo diz quantos, quantos em risco e o mais atrasado', () => {
    expect(summaryLine([])).toBe('Nenhum projeto no período.')
    expect(summaryLine([project()])).toBe('1 projeto no período; nenhum em risco.')
    const late = project({ projectId: 'p2', position: 2, projectNumber: 'PRJ-2026-0001', projectName: 'Ed. Aurora', risk: 'AtRisk', slackDays: -7, daysLate: 7 })
    const less = project({ projectId: 'p3', position: 3, risk: 'Late', slackDays: -2, daysLate: 2 })
    expect(summaryLine([project(), late, less])).toBe('3 projetos no período; 2 em risco. O mais atrasado é PRJ-2026-0001 · Ed. Aurora, 7 dias depois do prometido.')
  })

  it('CA 1: chip de folga com o sinal e o tom', () => {
    expect(slackText(3)).toBe('+3 dias')
    expect(slackText(-1)).toBe('−1 dia')
    expect(slackChip(project())).toEqual({ tone: 'ok', text: '+3 dias' })
    expect(slackChip(project({ slackDays: -7, risk: 'AtRisk' }))).toEqual({ tone: 'warn', text: '−7 dias' })
    expect(slackChip(project({ slackDays: -7, risk: 'Late' }))?.tone).toBe('crit')
    expect(slackChip(project({ slackDays: null, risk: 'Late' }))).toEqual({ tone: 'crit', text: 'Atrasado' })
  })

  it('CA 2: concluída com as datas reais e contínua; em andamento com a parte feita; prevista tracejada; sem previsão sem barra', () => {
    const p = project()
    const o = order()
    const done = stepBar(step({ state: 'Done', actualStart: '2026-10-28', actualEnd: '2026-10-29', plannedStart: null, plannedEnd: null }), o, p)!
    expect(done).toMatchObject({ kind: 'actual', start: '2026-10-28', end: '2026-10-29' })
    expect(done.ariaLabel).toContain('realizado de 28/10/2026 a 29/10/2026, concluída')
    const running = stepBar(step({ state: 'InProgress', progress: 0.5, remainingHours: 3, actualStart: '2026-10-30', plannedStart: '2026-10-30', plannedEnd: '2026-11-02' }), o, p)!
    expect(running).toMatchObject({ kind: 'progress', progress: 0.5, start: '2026-10-30' })
    expect(running.ariaLabel).toContain('em andamento, 50% feito')
    expect(running.detail.lines).toContain('Horas restantes: 3 de 6 h')
    const planned = stepBar(step({}), o, p)!
    expect(planned.kind).toBe('planned')
    expect(planned.ariaLabel).toBe('Etapa C · Corte, Corte, OS OS-2026-0001, previsto de 03/11/2026 a 04/11/2026, prevista')
    expect(stepBar(step({ state: 'NoForecast', plannedStart: null, plannedEnd: null, noForecastReason: 'Etapa Corte sem centro de trabalho' }), o, p)).toBeNull()
  })

  it('CA 3: a etapa que define a entrega leva o rótulo e o destaque; o projeto em risco tem a faixa âmbar', () => {
    const drive = stepBar(step({ drivesDelivery: true }), order(), project())!
    expect(drive).toMatchObject({ emphasis: true, label: 'Define a entrega' })
    expect(drive.ariaLabel).toMatch(/define a entrega$/)
    const rows = projectRows(view([project({ risk: 'AtRisk', slackDays: -7 }), project({ projectId: 'p2', risk: 'Late', slackDays: -9 })]), new Set())
    expect(rows.map((r) => [r.stripe, r.stripeLabel])).toEqual([['warn', 'Em risco'], ['crit', 'Atrasado']])
  })

  it('CA 1: a faixa do projeto vai do início ao previsto, com a marca do prometido', () => {
    const p = project()
    expect(projectBar(p, '2026-10-30')).toMatchObject({ start: '2026-11-03', end: '2026-11-10', kind: 'summary', inert: true })
    const [row] = projectRows(view([p]), new Set())
    expect(row.markers).toEqual([{ date: '2026-11-13', label: 'Prometido 13/11' }])
    expect(row.chip).toEqual({ tone: 'ok', text: '+3 dias' })
    expect(row.expanded).toBe(false)
  })

  it('CA 4: a OS de instalação é só leitura, hachurada, com as datas digitadas', () => {
    const installation = order({ orderId: 'i1', number: 'OSI-0001', kind: 'Installation', start: '2026-12-01', end: '2026-12-05', steps: [] })
    const bar = orderBar(installation, project())!
    expect(bar).toMatchObject({ kind: 'readonly', label: 'Instalação (datas da OS)', start: '2026-12-01', end: '2026-12-05' })
    expect(bar.ariaLabel).toContain('só leitura')
    expect(orderOfBar(bar.id, view([project({ orders: [installation] })]), null)).toEqual({ orderId: 'i1', installation: true })
  })

  it('projeto → OS → etapa só no que está aberto; acima de 50 projetos tudo recolhido (CA 13)', () => {
    const v = view([project()])
    expect(projectRows(v, defaultOpen(v)).map((r) => r.id)).toEqual(['p:p1', 'o:o1', 's:s1'])
    expect(projectRows(v, new Set(['p:p1'])).map((r) => r.id)).toEqual(['p:p1', 'o:o1'])
    const many = view(Array.from({ length: COLLAPSE_ABOVE + 1 }, (_, i) => project({ projectId: `p${i}` })))
    expect(defaultOpen(many).size).toBe(0)
    expect(orderOfBar('s:s1', v, null)).toEqual({ orderId: 'o1', installation: false })
  })

  it('CA 5: o dia do centro diz a ocupação e o feriado em texto', () => {
    const day = (over: Partial<GanttCenterDay>): GanttCenterDay => ({ date: '2026-11-05', allocatedHours: 8, capacityHours: 8, percent: 100, band: 'Full', exceptionType: null, exceptionName: null, ...over })
    expect(centerDay(day({}))).toEqual({ date: '2026-11-05', tone: 'full', title: '05/11: 8 de 8 h, 100%, Lotado' })
    expect(centerDay(day({ date: '2026-11-02', allocatedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity', exceptionType: 'Holiday', exceptionName: 'Finados' })))
      .toMatchObject({ tone: 'off', mark: 'Finados', title: '02/11: Feriado Finados' })
    expect(centerDay(day({ allocatedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity' })).title).toBe('05/11: sem expediente')
    const centers = [{ workCenterId: 'w1', name: 'Corte', line: 'Cabines', lanes: 2, days: [day({})], bars: [
      { stepId: 's1', stepName: 'Corte', code: 'C', orderId: 'o1', orderNumber: 'OS-1', projectId: 'p1', projectNumber: 'PRJ-1', projectName: null, state: 'InProgress' as const, standardHours: 6, remainingHours: 2, progress: 0.6667, plannedStart: '2026-10-30', plannedEnd: '2026-10-30', actualStart: '2026-10-29', lane: 1 },
    ] }]
    const [row] = centerRows(centers)
    expect(row).toMatchObject({ lanes: 2, sublabel: 'Cabines · 1 etapa · lotado em 1 dia' })
    expect(row.bars[0]).toMatchObject({ lane: 1, kind: 'progress', start: '2026-10-29', letter: 'C' })
    expect(centersSummary(centers)).toBe('1 centro; 1 etapa no período. Mais ocupado: Corte, lotado em 1 dia.')
  })

  it('CA 6 e 9: filtros e escala na URL, só o que difere do padrão', () => {
    const f = parseGanttFilters({ aba: 'cronograma', visao: 'centro', escala: 'mes', periodo: '6m', linha: 'Cabines', risco: '1' })
    expect(f).toMatchObject({ view: 'centro', scale: 'mes', period: '6m', line: 'Cabines', atRisk: true })
    expect(ganttFiltersToSearch(parseGanttFilters({}))).toEqual({ visao: 'projeto', escala: undefined, periodo: undefined, projeto: undefined, cliente: undefined, linha: undefined, centro: undefined, risco: undefined })
    expect(parseGanttFilters({ visao: 'grafico', escala: 'x' })).toMatchObject({ view: 'projeto', scale: 'semana' })
    expect(periodRange('12s', '2026-10-30')).toEqual({})
    expect(periodRange('12m', '2026-10-30')).toEqual({ from: '2026-10-23', to: '2027-10-23' })
  })

  it('400 px: a barra simples ocupa a fração do período', () => {
    expect(simpleBar('2026-11-01', '2026-11-10', '2026-11-01', '2026-11-30')).toEqual({ left: 0, width: (10 / 30) * 100 })
    expect(simpleBar('2026-12-01', '2026-12-02', '2026-11-01', '2026-11-30')).toBeNull()
  })
})
