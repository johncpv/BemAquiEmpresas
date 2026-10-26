import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { type TreeNode, TreeTable } from './tree-table'

afterEach(cleanup)

interface Row extends TreeNode {
  values: number[]
  children?: Row[]
}

const NODES: Row[] = [
  { id: 'g', label: 'Receita bruta', values: [10, 20], children: [{ id: 'a', label: '1.1 Vendas', values: [10, 20] }] },
  { id: 's', label: '= Resultado', values: [-1, 5] },
]

function renderGrid(onActivate = vi.fn()) {
  render(
    <TreeTable<Row> label="DRE" nodes={NODES} cellNavigation stickyLabel searchable={false} defaultExpanded="none" labelHeader="Linha"
      columns={[{ key: 'm1', header: 'jan', numeric: true, cell: (n) => n.values[0] }, { key: 'm2', header: 'fev', numeric: true, cell: (n) => n.values[1] }]}
      canActivate={(n) => n.id !== 's'} onActivateCell={onActivate} />,
  )
  return onActivate
}

const cell = (text: string) => screen.getByText(text).closest('[role="gridcell"]') as HTMLElement
const active = () => document.activeElement as HTMLElement

describe('TreeTable analítica (D18, FIN-08 CA 12)', () => {
  it('treegrid com aria-rowindex, aria-colindex e a coluna de nomes fixa', () => {
    renderGrid()
    const grid = screen.getByRole('treegrid', { name: 'DRE' })
    expect(grid.getAttribute('aria-colcount')).toBe('3')
    const label = cell('Receita bruta')
    expect(label.getAttribute('aria-colindex')).toBe('1')
    expect(label.closest('tr')?.getAttribute('aria-rowindex')).toBe('2')
    expect(label.className).toContain('sticky')
    expect(label.tabIndex).toBe(0)
    expect(screen.queryByText('1.1 Vendas')).toBeNull()
  })

  it('setas movem a célula ativa; direita e esquerda abrem e fecham o grupo na coluna de nomes; Home e End', () => {
    renderGrid()
    const label = cell('Receita bruta')
    act(() => label.focus())
    fireEvent.keyDown(label, { key: 'ArrowRight' })
    expect(screen.queryByText('1.1 Vendas')).not.toBeNull()
    expect(label.closest('tr')?.getAttribute('aria-expanded')).toBe('true')
    fireEvent.keyDown(label, { key: 'ArrowRight' })
    expect(active().getAttribute('aria-colindex')).toBe('2')
    expect(active().textContent).toBe('10')
    fireEvent.keyDown(active(), { key: 'End' })
    expect(active().getAttribute('aria-colindex')).toBe('3')
    fireEvent.keyDown(active(), { key: 'ArrowDown' })
    expect(active().closest('tr')?.getAttribute('aria-rowindex')).toBe('3')
    expect(active().textContent).toBe('20')
    fireEvent.keyDown(active(), { key: 'Home' })
    expect(active().textContent).toBe('1.1 Vendas')
    fireEvent.keyDown(active(), { key: 'ArrowLeft' })
    expect(active().textContent).toBe('Receita bruta')
    fireEvent.keyDown(active(), { key: 'ArrowLeft' })
    expect(screen.queryByText('1.1 Vendas')).toBeNull()
  })

  it('Enter e clique abrem o drill-down só nas células que aceitam', () => {
    const onActivate = renderGrid()
    const value = screen.getByText('-1').closest('td') as HTMLElement
    act(() => value.focus())
    fireEvent.keyDown(value, { key: 'Enter' })
    expect(onActivate).not.toHaveBeenCalled()
    const revenue = screen.getByText('20').closest('td') as HTMLElement
    fireEvent.click(revenue)
    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ id: 'g' }), 1)
    act(() => revenue.focus())
    fireEvent.keyDown(revenue, { key: 'Enter' })
    expect(onActivate).toHaveBeenCalledTimes(2)
    expect(revenue.className).toContain('focus-visible:outline-ring')
  })

  it('sem cellNavigation continua a árvore por linha (FND-09d)', () => {
    render(<TreeTable label="Plano" nodes={[{ id: '1', label: 'Receitas', children: [{ id: '1.1', label: 'Vendas' }] }]} />)
    const root = screen.getByText('Receitas').closest('tr')!
    expect(root.tabIndex).toBe(0)
    expect(root.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('searchbox')).not.toBeNull()
  })
})
