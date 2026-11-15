import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { MarginOverview } from '@/api/generated/model'
import { MarginKpi } from './owner-cards'

const state = vi.hoisted(() => ({ card: undefined as unknown }))

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetDashboardMargin: () => ({ data: state.card, isPending: false, isError: false, refetch: vi.fn() }),
}))
vi.mock('@tanstack/react-router', async (original) => ({
  ...(await original<typeof import('@tanstack/react-router')>()),
  useNavigate: () => vi.fn(),
  Link: ({ to, params, search, children, ...rest }: { to: string; params?: { id?: string }; search?: Record<string, string>; children: ReactNode }) => (
    <a href={to.replace('$id', params?.id ?? '') + (search ? '?' + new URLSearchParams(search).toString() : '')} {...rest}>{children}</a>
  ),
}))
vi.mock('@/pages/use-can', () => ({ useCan: () => false }))

const base: MarginOverview = {
  year: 2026, activeCount: 2, activeRevenue: 100_000, projectedMarginPct: 38.5, budgetedMarginPct: 40, deviationPp: -1.5, finishedCount: 0, finishedRealizedMarginPct: null,
  finishedBudgetedMarginPct: null, finishedDeviationPp: null, worst: [], updatedAt: '2026-11-15T12:00:00Z', unbudgetedCount: 0, unbudgeted: [],
}
const prj = { projectId: 'p1', number: 'PRJ-2026-0001', name: 'Elevador' }

describe('card Margem dos projetos: projeto sem orçamento de custo', () => {
  it('todos com orçamento: a margem, sem alerta', () => {
    state.card = base
    render(<MarginKpi />)
    expect(screen.getByText('38,5%')).toBeTruthy()
    expect(screen.queryByText(/sem orçamento de custo/)).toBeNull()
  })

  it('um sem orçamento entre os em andamento: a margem dos outros e o alerta que leva ao projeto', () => {
    state.card = { ...base, unbudgetedCount: 1, unbudgeted: [prj] }
    render(<MarginKpi />)
    expect(screen.getByText('38,5%')).toBeTruthy()
    expect(screen.getByRole('note').textContent).toContain('1 projeto sem orçamento de custo: a margem não é confiável.')
    expect(screen.getByRole('link', { name: 'Completar o orçamento de PRJ-2026-0001' }).getAttribute('href')).toBe('/app/projetos/p1?aba=custos')
  })

  it('todos sem orçamento: nenhum percentual (nem 100%), só o alerta com a ação', () => {
    state.card = { ...base, activeCount: 1, activeRevenue: 0, projectedMarginPct: null, budgetedMarginPct: null, deviationPp: null, unbudgetedCount: 1, unbudgeted: [prj] }
    const { container } = render(<MarginKpi />)
    expect(screen.queryByText('Margem projetada (com custos reais)')).toBeNull()
    expect(container.textContent).not.toMatch(/100[,.]0%/)
    expect(screen.getByRole('link', { name: 'Completar o orçamento de PRJ-2026-0001' })).toBeTruthy()
  })

  it('vários sem orçamento: leva ao Orçado × realizado só com eles', () => {
    state.card = { ...base, activeCount: 5, unbudgetedCount: 3, unbudgeted: [prj, { ...prj, projectId: 'p2' }, { ...prj, projectId: 'p3' }] }
    render(<MarginKpi />)
    expect(screen.getByRole('note').textContent).toContain('3 projetos sem orçamento de custo')
    expect(screen.getByRole('link', { name: 'Completar o orçamento' }).getAttribute('href')).toBe('/app/projetos/margens?semOrcamento=true')
  })
})
