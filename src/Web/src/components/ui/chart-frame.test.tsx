import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { cloneElement, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { chartPoints, chartSeries, chartSummary } from '@/pages/finance/cash-flow/cash-flow'
import { Chart } from './chart'
import { ChartFrame } from './chart-frame'

// O jsdom não mede o contêiner: o gráfico desenha com 800 × 260 para os rótulos e os traços aparecerem.
vi.mock('recharts', async (original) => {
  const actual = await original<typeof import('recharts')>()
  return { ...actual, ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) => <div>{cloneElement(children, { width: 800, height: 260 })}</div> }
})

const data = [
  { dia: '20/10', real: 100, base: undefined, aj: undefined },
  { dia: '21/10', real: 120, base: 120, aj: 120 },
  { dia: '22/10', real: undefined, base: -50, aj: -300 },
]

describe('ChartFrame (FIN-07 CA 11)', () => {
  it('título, resumo acima, legenda e a tabela equivalente aberta pelo teclado', async () => {
    render(
      <ChartFrame title="Caixa" summary="Menor saldo ajustado em 22/10: −R$ 300,00." legend={[{ key: 'a', label: 'Ajustado', color: 'var(--chart-3)', dash: '10 4', width: 3.5, marker: true }]}
        table={{ xLabel: 'Dia', columns: [{ key: 'a', label: 'Ajustado' }], rows: [{ x: '22/10', values: { a: 'R$ -300' } }] }}>
        {(id) => <svg aria-describedby={id} />}
      </ChartFrame>,
    )
    const figure = screen.getByRole('figure', { name: 'Caixa' })
    const summary = screen.getByTestId('chart-summary')
    expect(summary.textContent).toContain('Menor saldo ajustado')
    expect(summary.compareDocumentPosition(screen.getByRole('img', { name: 'Caixa' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(figure.querySelector('svg circle')).not.toBeNull()
    const button = screen.getByRole('button', { name: 'Ver como tabela' })
    expect(button.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('region', { name: 'Caixa: tabela' })).toBeNull()
    button.focus()
    fireEvent.click(button)
    const region = await screen.findByRole('region', { name: 'Caixa: tabela' })
    await waitFor(() => expect(document.activeElement).toBe(region))
    expect(region.textContent).toContain('R$ -300')
    expect(screen.getByRole('button', { name: 'Ocultar a tabela' }).getAttribute('aria-expanded')).toBe('true')
  })

  it('estados carregando, vazio e erro', () => {
    const props = { title: 'Caixa', legend: [], table: { xLabel: 'Dia', columns: [], rows: [] }, children: () => null }
    const { rerender, container } = render(<ChartFrame {...props} state="loading" />)
    expect(container.querySelector('.animate-pulse')).not.toBeNull()
    rerender(<ChartFrame {...props} state="empty" emptyTitle="Sem movimento ainda." />)
    expect(screen.getByRole('status').textContent).toContain('Sem movimento ainda.')
    const retry = vi.fn()
    rerender(<ChartFrame {...props} state="error" onRetry={retry} />)
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(retry).toHaveBeenCalled()
  })
})

describe('Chart dentro do ChartFrame (FIN-07 CA 11 e 12)', () => {
  const series = [
    { key: 'real', label: 'Realizado', kind: 'actual' as const, color: 1 },
    { key: 'base', label: 'Base', kind: 'base' as const, color: 2 },
    { key: 'aj', label: 'Ajustado', kind: 'adjusted' as const, color: 3 },
  ]

  it('rótulo direto no fim de cada série, referências em x e em y e traços distintos', () => {
    const { container } = render(<Chart title="Saldo" xKey="dia" data={data} series={series} references={[{ x: '21/10', label: 'Hoje' }]} yReferences={[{ y: 0, label: 'Saldo zero' }]} summary="Resumo" />)
    const labels = [...container.querySelectorAll('[data-series-label]')].map((l) => l.textContent)
    expect(labels).toEqual(['Realizado', 'Base', 'Ajustado'])
    expect(container.textContent).toContain('Hoje')
    expect(container.textContent).toContain('Saldo zero')
    const strokes = [...container.querySelectorAll('.recharts-line-curve')].map((p) => `${p.getAttribute('stroke-dasharray') ?? 'contínuo'}|${p.getAttribute('stroke-width')}`)
    expect(new Set(strokes).size).toBe(3)
    expect(strokes[0]).toMatch(/^contínuo/)
    expect(container.querySelectorAll('.recharts-line-dot').length).toBeGreaterThan(0)
    expect(container.innerHTML).not.toMatch(/#[0-9a-f]{6}/i)
  })

  it('a legenda repete o traço: nenhuma série só pela cor', () => {
    render(<Chart title="Saldo" xKey="dia" data={data} series={series} />)
    const swatches = [...screen.getByRole('list', { name: 'Legenda' }).querySelectorAll('line')].map((l) => `${l.getAttribute('stroke-dasharray')}|${l.getAttribute('stroke-width')}`)
    expect(new Set(swatches).size).toBe(3)
  })

  it('sem dados mostra o vazio', () => {
    render(<Chart title="Saldo" xKey="dia" data={[]} series={series} emptyTitle="Nada para mostrar." />)
    expect(screen.getByRole('status').textContent).toContain('Nada para mostrar.')
  })
})

describe('cenários do fluxo (FIN-07 CA 7, 11 e 12)', () => {
  const period = (start: string, balance: number, projected: boolean, today = false) => ({ start, end: start, inflow: 0, outflow: 0, balance, projected, containsToday: today, overdueInflow: 0, overdueOutflow: 0 })
  const base = [period('2026-10-20', 100, false), period('2026-10-21', 120, true, true), period('2026-10-22', -50, true)]
  const adjusted = { periods: [period('2026-10-20', 100, false), period('2026-10-21', 120, true, true), period('2026-10-22', -300, true)], lowest: { date: '2026-10-22', balance: -300 }, firstNegative: null, lowestDifference: 250, shiftedBeyond: { count: 0, total: 0 }, atRiskTotal: 0, atRisk: [], tenantHasHistory: true, tenantAvgDelayDays: 6, thresholdPct: 20 }

  it('os dois: realizado contínuo, base tracejado fino e ajustado tracejado grosso', () => {
    expect(chartSeries('both', true).map((s) => s.kind)).toEqual(['actual', 'base', 'adjusted'])
    expect(chartSeries('adjusted', true).map((s) => s.kind)).toEqual(['actual', 'adjusted'])
    expect(chartSeries('both', false).map((s) => s.kind)).toEqual(['actual', 'projected'])
    expect(chartPoints(base, 'daily', adjusted).map((p) => p.ajustado)).toEqual([undefined, 120, -300])
  })

  it('o resumo textual cita os dois menores saldos', () => {
    const text = chartSummary({ lowest: { date: '2026-10-22', balance: -50 } }, adjusted)
    expect(text).toMatch(/^Menor saldo ajustado em 22\/10\/2026: .*300,00; na base, em 22\/10\/2026: .*50,00\.$/)
  })
})
