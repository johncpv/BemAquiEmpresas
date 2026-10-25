import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FactoryLoadCell, FactoryLoadRow, FactoryLoadWeek, FactoryLoadWeekly } from '@/api/generated/model'
import { Chart } from '@/components/ui/chart'
import {
  afterRows, bandChip, busyLine, chartData, filtersToSearch, hoursOf, isEmptyPlan, overCount, parseFilters, percentText, planLine, shortDate, summaryLine, weekLabel,
} from './load'
import { OccupancyGrid } from './OccupancyGrid'

afterEach(cleanup)

const week = (start: string, end: string, isoWeek: number, from = start): FactoryLoadWeek => ({ start, end, isoWeek, from })
const cell = (weekStart: string, committedHours: number, capacityHours: number, percent: number | null, band: FactoryLoadCell['band']): FactoryLoadCell =>
  ({ weekStart, committedHours, capacityHours, percent, band })

// O cenário fixo da PLN-01a CA 7 (CA 2): Corte e Montagem nas semanas de 26/10 e 02/11.
const WEEKS = [week('2026-10-26', '2026-11-01', 44, '2026-10-30'), week('2026-11-02', '2026-11-08', 45)]
const CUT: FactoryLoadRow = { workCenterId: 'cut', name: 'Corte', line: 'Cabines', hoursAfter: 0, busyUntil: '2026-11-04', cells: [cell(WEEKS[0].start, 8, 8, 100, 'Full'), cell(WEEKS[1].start, 10, 32, 31.3, 'Idle')] }
const ASSEMBLY: FactoryLoadRow = { workCenterId: 'asm', name: 'Montagem', line: 'Cabines', hoursAfter: 0, busyUntil: '2026-11-05', cells: [cell(WEEKS[0].start, 0, 16, 0, 'Idle'), cell(WEEKS[1].start, 34, 64, 53.1, 'Idle')] }
const FACTORY: FactoryLoadRow = { workCenterId: null, name: 'Fábrica', line: null, hoursAfter: 0, busyUntil: '2026-11-05', cells: [cell(WEEKS[0].start, 8, 24, 33.3, 'Idle'), cell(WEEKS[1].start, 44, 96, 45.8, 'Idle')] }
const LOAD: FactoryLoadWeekly = { today: '2026-10-30', planComputedAt: '2026-10-30T17:05:00Z', weeks: WEEKS, centers: [CUT, ASSEMBLY], factory: FACTORY }

describe('ocupação (PLN-04)', () => {
  it('rótulos da semana, do percentual e das horas', () => {
    expect(weekLabel(WEEKS[1])).toBe('S45 · 02/11')
    expect(percentText(31.3)).toBe('31,3%')
    expect(percentText(100)).toBe('100,0%')
    expect(percentText(null)).toBe('—')
    expect(hoursOf(CUT.cells[1])).toBe('10 de 32 h')
    expect(hoursOf({ committedHours: 34.25, capacityHours: 64 })).toBe('34,3 de 64 h')
  })

  it('faixas com o texto junto da cor (CA 4 e 6)', () => {
    expect(bandChip('Idle')).toEqual({ tone: 'info', label: 'Folga' })
    expect(bandChip('Normal')).toBeNull()
    expect(bandChip('Full')).toEqual({ tone: 'warn', label: 'Lotado' })
    expect(bandChip('Over')).toEqual({ tone: 'crit', label: 'Acima da capacidade' })
    expect(bandChip('NoCapacity')).toBeNull()
    expect(overCount([{ ...CUT, cells: [cell(WEEKS[0].start, 8, 4, 200, 'Over'), CUT.cells[1]] }, FACTORY])).toBe(1)
  })

  it('filtros na URL só com o que difere do padrão (CA 9)', () => {
    expect(parseFilters({})).toEqual({ weeks: 8, line: undefined, center: undefined, view: 'tabela', week: undefined })
    const f = parseFilters({ semanas: 26, linha: 'Cabines', centro: 'cut', visao: 'grafico' })
    expect(f).toMatchObject({ weeks: 26, line: 'Cabines', center: 'cut', view: 'grafico' })
    expect(parseFilters({ semanas: '27' }).weeks).toBe(8)
    expect(parseFilters({ semanas: '0' }).weeks).toBe(8)
    expect(filtersToSearch({ weeks: 8, view: 'tabela' })).toEqual({ semanas: undefined, linha: undefined, centro: undefined, visao: undefined, semana: undefined })
    expect(filtersToSearch(f)).toMatchObject({ semanas: 26, linha: 'Cabines', centro: 'cut', visao: 'grafico' })
  })

  it('"Depois de", ocupado até e o resumo do gráfico (CA 3 e 8)', () => {
    const long = { ...CUT, hoursAfter: 320, busyUntil: '2027-02-19' }
    expect(afterRows([ASSEMBLY, long]).map((r) => r.name)).toEqual(['Corte'])
    expect(shortDate('2026-12-20', LOAD.today)).toBe('20/12')
    expect(shortDate('2027-02-19', LOAD.today)).toBe('19/02/2027')
    expect(busyLine([ASSEMBLY, CUT], LOAD.today)).toBe('Montagem ocupado até 05/11')
    expect(summaryLine(LOAD, FACTORY, [CUT, ASSEMBLY])).toBe('Semana mais ocupada: S45 · 02/11, 45,8%. Montagem ocupado até 05/11.')
    expect(summaryLine(LOAD, CUT, [CUT])).toBe('Semana mais ocupada: S44 · 26/10, 100,0%. Corte ocupado até 04/11.')
    expect(chartData(LOAD, CUT)).toEqual([{ semana: 'S44 · 26/10', ocupacao: 100 }, { semana: 'S45 · 02/11', ocupacao: 31.3 }])
    expect(isEmptyPlan(LOAD)).toBe(false)
    expect(isEmptyPlan({ ...LOAD, factory: { ...FACTORY, cells: FACTORY.cells.map((c) => ({ ...c, committedHours: 0 })) } })).toBe(true)
  })

  it('"Plano de {hh:mm}" (hoje) e com a data em outro dia', () => {
    expect(planLine('2026-10-30T17:05:00Z', '2026-10-30')).toBe('Plano de 14:05')
    expect(planLine('2026-10-29T17:05:00Z', '2026-10-30')).toBe('Plano de 29/10/2026 14:05')
    expect(planLine(null, '2026-10-30')).toBe('O plano ainda não foi calculado.')
  })

  it('tabela navega por célula pelo teclado e Enter abre o drill-down (CA 7)', () => {
    const onOpen = vi.fn()
    render(<OccupancyGrid label="Ocupação por semana e centro" weeks={WEEKS} rows={[CUT, ASSEMBLY, FACTORY]} onOpen={onOpen} />)
    const grid = screen.getByRole('grid', { name: 'Ocupação por semana e centro' })
    const cells = screen.getAllByRole('gridcell')
    expect(cells).toHaveLength(6)
    expect(cells.filter((c) => c.tabIndex === 0)).toHaveLength(1)
    expect(grid.querySelector('th[scope="row"]')!.className).toContain('sticky')
    expect(screen.getByRole('gridcell', { name: 'Corte, S44 · 26/10: 8 de 8 h, 100,0%, Lotado' })).toBeTruthy()

    cells[0].focus()
    fireEvent.keyDown(cells[0], { key: 'ArrowRight' })
    expect(document.activeElement).toBe(cells[1])
    fireEvent.keyDown(cells[1], { key: 'ArrowDown' })
    expect(document.activeElement).toBe(cells[3])
    fireEvent.keyDown(cells[3], { key: 'Home' })
    expect(document.activeElement).toBe(cells[2])
    fireEvent.keyDown(cells[2], { key: 'End', ctrlKey: true })
    expect(document.activeElement).toBe(cells[5])
    fireEvent.keyDown(cells[5], { key: 'ArrowDown' })
    expect(document.activeElement).toBe(cells[5]) // para na última linha
    fireEvent.keyDown(cells[5], { key: 'Enter' })
    expect(onOpen).toHaveBeenCalledWith(FACTORY, 1)
    fireEvent.click(cells[0])
    expect(onOpen).toHaveBeenLastCalledWith(CUT, 0)
    expect(cells[0].tabIndex).toBe(0)
  })

  it('gráfico com a referência "Capacidade (100%)" na legenda e a tabela equivalente (CA 8)', () => {
    render(<Chart title="Ocupação da fábrica por semana" data={chartData(LOAD, FACTORY)} xKey="semana" xLabel="Semana" series={[{ key: 'ocupacao', label: 'Ocupação' }]}
      format={(v) => percentText(v)} yReferences={[{ y: 100, label: 'Capacidade (100%)', extend: true, legend: true }]} valueLabels />)
    expect(screen.getByRole('img', { name: 'Ocupação da fábrica por semana' })).toBeTruthy()
    expect(screen.getByText('Capacidade (100%)')).toBeTruthy()
    const table = screen.getByRole('table', { hidden: true })
    expect(table.textContent).toContain('S45 · 02/11')
    expect(table.textContent).toContain('45,8%')
  })
})
