import { fireEvent, render, screen } from '@testing-library/react'
import { cloneElement, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { buildNavigation } from '@/navigation/modules'
import { marginChip, marginContext, pct1, pp, worstSummary } from '@/pages/projects/margins/margins'
import { ColumnChart } from './ui/column-chart'
import { DashboardCard } from './ui/dashboard-card'
import { Kpi } from './ui/kpi'

// jsdom não mede o contêiner: o gráfico desenha com tamanho fixo no teste.
vi.mock('recharts', async (original) => {
  const actual = await original<typeof import('recharts')>()
  return { ...actual, ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) => <div>{cloneElement(children, { width: 640, height: 280 })}</div> }
})

describe('Kpi (DSH-02 CA 16)', () => {
  it('mostra rótulo, valor com tabular-nums, contexto e medidor; clicável até a origem', () => {
    const onClick = vi.fn()
    render(<Kpi label="Margem" value="19,4%" context="−3,6 pp abaixo dos 23,0% orçados" meter={{ value: 19.4, max: 23, label: 'Margem projetada sobre a orçada', tone: 'warn' }} onClick={onClick} actionLabel="ver origem" />)
    const button = screen.getByRole('button', { name: /Margem.*19,4%.*ver origem/ })
    expect(button.querySelector('.tabular-nums')?.textContent).toBe('19,4%')
    expect(screen.getByText('−3,6 pp abaixo dos 23,0% orçados')).toBeTruthy()
    expect(screen.getByRole('meter', { name: 'Margem projetada sobre a orçada' }).getAttribute('aria-valuenow')).toBe('19.4')
    fireEvent.click(button)
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('sem clique é só leitura', () => {
    render(<Kpi label="Receita" value="R$ 10" />)
    expect(screen.queryByRole('button')).toBeNull()
  })
})

describe('DashboardCard (DSH-02 CA 16)', () => {
  const ok = { isPending: false, isError: false, refetch: vi.fn() }

  it('título, Como calculamos, Ver origem e Atualizado às', () => {
    const onOrigin = vi.fn()
    render(<DashboardCard title="Consolidado" subtitle="Projetos do filtro" howWeCalculate="Σ ÷ Σ" onOrigin={onOrigin} updatedAt={Date.UTC(2026, 9, 25, 13, 5)} query={ok}><p>conteúdo</p></DashboardCard>)
    expect(screen.getByRole('region', { name: 'Consolidado' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Dica: Como calculamos: Consolidado' })).toBeTruthy()
    expect(screen.getByText('Atualizado às 10:05')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Ver origem' }))
    expect(onOrigin).toHaveBeenCalledOnce()
    expect(screen.getByText('conteúdo')).toBeTruthy()
  })

  it('carregando, vazio, erro com Tentar de novo e sem permissão', () => {
    const { rerender, container } = render(<DashboardCard title="Margem" query={{ ...ok, isPending: true }} />)
    expect(screen.getByLabelText('Carregando Margem')).toBeTruthy()
    rerender(<DashboardCard title="Margem" query={ok} empty={{ when: true, title: 'Nenhum projeto em andamento.' }} />)
    expect(screen.getByText('Nenhum projeto em andamento.')).toBeTruthy()
    const refetch = vi.fn()
    rerender(<DashboardCard title="Margem" query={{ isPending: false, isError: true, refetch }} />)
    expect(screen.getByText('Não foi possível carregar margem.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(refetch).toHaveBeenCalledOnce()
    rerender(<DashboardCard title="Margem" query={ok} allowed={false}><p>segredo</p></DashboardCard>)
    expect(container.innerHTML).toBe('')
  })
})

describe('ColumnChart (DSH-02 CA 17)', () => {
  const data = [{ nature: 'Material', budgeted: 30000, projected: 33750 }, { nature: 'Frete', budgeted: 1000, projected: 900 }]
  const series = [{ key: 'budgeted', label: 'Orçado', fill: 'outline' as const }, { key: 'projected', label: 'Projetado', fill: 'solid' as const }]

  it('preenchimento distinto, rótulo direto, referência zero, resumo e tabela equivalente', () => {
    const { container } = render(<ColumnChart title="Orçado × projetado por natureza" xKey="nature" xLabel="Natureza" data={data} series={series} summary="A natureza que mais desviou foi Material: R$ 3.750,00 acima do orçado." format={(v) => `R$ ${v}`} />)
    expect(screen.getByText('A natureza que mais desviou foi Material: R$ 3.750,00 acima do orçado.')).toBeTruthy()
    const img = screen.getByRole('img', { name: 'Orçado × projetado por natureza' })
    const table = document.getElementById(img.getAttribute('aria-describedby') ?? '')
    expect(table?.tagName).toBe('TABLE')
    expect(table?.textContent).toContain('R$ 33750')
    // A série não é só cor: legenda com o tipo de coluna; vazada sem preenchimento.
    expect(screen.getByText(/Orçado \(coluna vazada\)/)).toBeTruthy()
    expect(screen.getByText(/Projetado \(coluna cheia\)/)).toBeTruthy()
    const legend = container.querySelectorAll('figcaption rect')
    expect(legend[0].getAttribute('fill')).toBe('none')
    expect(legend[1].getAttribute('fill')).toBe('var(--chart-2)')
    // Rótulo direto em cada coluna e a referência "zero".
    const svg = container.querySelector('svg.recharts-surface')
    expect(svg).toBeTruthy()
    expect(Array.from(container.querySelectorAll('.recharts-label-list')).map((l) => l.textContent).join(' ')).toContain('R$ 33750')
    expect(svg?.textContent).toContain('zero')
    expect(container.innerHTML).not.toMatch(/#[0-9a-f]{3,6}\b/i)
  })

  it('aceita a referência de capacidade ou meta (PLN-04, DSH-01b)', () => {
    const { container } = render(<ColumnChart title="Ocupação" xKey="nature" data={data} series={series} references={[{ y: 20000, label: 'capacidade' }]} />)
    expect(container.textContent).toContain('capacidade')
  })
})

describe('margens na tela (DSH-02 CA 6, 11)', () => {
  it('1 casa e pontos percentuais com sinal', () => {
    expect(pct1(71.6993)).toBe('71,7%')
    expect(pct1(null)).toBe('—')
    expect(pp(-3.3007)).toBe('−3,3 pp')
    expect(marginContext({ budgetedMarginPct: 23, marginDeviationPp: -3.6 })).toBe('−3,6 pp abaixo dos 23,0% orçados')
  })

  it('chip de alerta e de prejuízo', () => {
    expect(marginChip({ projectedMarginPct: 20, marginDeviationPp: -3.5 })).toEqual({ tone: 'warn', text: 'Margem 3,5 pp abaixo' })
    expect(marginChip({ projectedMarginPct: -2, marginDeviationPp: -30 })).toEqual({ tone: 'crit', text: 'Prejuízo projetado' })
    expect(marginChip({ projectedMarginPct: 25, marginDeviationPp: 0 })).toBeNull()
  })

  it('resumo do gráfico pela natureza que mais desviou', () => {
    const line = (costType: 'Material' | 'Freight', deviation: number) => ({ costType, budgeted: 0, realized: 0, committed: 0, projected: 0, deviation, deviationPct: null, includesBackfill: false })
    expect(worstSummary({ lines: [line('Freight', 180.4), line('Material', 2750)] })).toMatch(/^A natureza que mais desviou foi Material: R\$\s2\.750,00 acima do orçado\.$/)
  })
})

describe('menu "Orçado × realizado" (DSH-02 CA 19)', () => {
  const nav = (actions: string[]) => buildNavigation([{ code: 'projects', name: 'Projetos', area: 'Comercial', released: true, enabled: true, actions }])

  it('aparece só com projects.view_cost', () => {
    expect(nav(['view', 'view_cost']).all[0].tabs.map((t) => t.slug)).toEqual(['margens'])
    expect(nav(['view', 'edit']).all[0].tabs).toEqual([])
  })
})
