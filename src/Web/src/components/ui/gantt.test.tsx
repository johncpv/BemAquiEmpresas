import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Gantt, type GanttBar, type GanttRow } from './gantt'

afterEach(cleanup)

const bar = (id: string, start: string, end: string, extra: Partial<GanttBar> = {}): GanttBar =>
  ({ id, start, end, kind: 'planned', ariaLabel: `Barra ${id}`, detail: { title: `Detalhe ${id}`, lines: ['Linha'] }, ...extra })

const ROWS: GanttRow[] = [
  { id: 'r1', label: 'Linha 1', depth: 0, bars: [bar('a', '2026-11-02', '2026-11-03'), bar('b', '2026-11-05', '2026-11-06', { kind: 'actual' })], markers: [{ date: '2026-11-10', label: 'Prometido 10/11' }] },
  { id: 'r2', label: 'Linha 2', depth: 1, bars: [bar('c', '2026-11-04', '2026-11-04', { kind: 'progress', progress: 0.5 })] },
  { id: 'r3', label: 'Linha 3', depth: 1, bars: [bar('d', '2026-11-20', '2026-11-21', { kind: 'readonly', label: 'Instalação (datas da OS)' })] },
]

function draw(onActivate = vi.fn(), rows = ROWS) {
  render(<Gantt title="Cronograma de teste" summary="3 projetos no período; nenhum em risco." rows={rows} from="2026-10-26" to="2026-11-30" today="2026-10-30" scale="semana"
    rowHeader="Projeto" legend={[{ sample: 'planned', label: 'Previsto' }]} onActivate={onActivate} table={<table><caption>Tabela equivalente</caption></table>} />)
  return onActivate
}

describe('Gantt (PLN-03 CA 2, 8 e 10, ADR 0033)', () => {
  it('desenha em SVG com role="img" ligado à tabela equivalente e o resumo acima', () => {
    draw()
    const img = screen.getByRole('img', { name: 'Cronograma de teste' })
    expect(img.tagName.toLowerCase()).toBe('svg')
    const table = document.getElementById(img.getAttribute('aria-describedby')!)!
    expect(table.textContent).toContain('Tabela equivalente')
    expect(table.hidden).toBe(true)
    expect(screen.getByTestId('gantt-resumo').textContent).toBe('3 projetos no período; nenhum em risco.')
    fireEvent.click(screen.getByRole('button', { name: 'Ver como tabela' }))
    expect(table.hidden).toBe(false)
    expect(screen.getByRole('button', { name: 'Ocultar a tabela' }).getAttribute('aria-expanded')).toBe('true')
  })

  it('real × previsto pelo traço: tracejado no previsto, contínuo no realizado, parte feita preenchida', () => {
    draw()
    const planned = document.querySelector('[data-bar="a"] rect:not(.gantt-ring)')!
    expect(planned.getAttribute('stroke-dasharray')).toBe('5 3')
    const actual = document.querySelector('[data-bar="b"] rect:not(.gantt-ring)')!
    expect(actual.getAttribute('stroke-dasharray')).toBeNull()
    expect(actual.getAttribute('class')).toContain('fill-primary')
    const done = document.querySelector('[data-bar="c"] [data-done]')!
    expect(Number(done.getAttribute('width'))).toBe(6)
    expect(document.querySelector('[data-bar="d"] text')?.textContent).toBe('Instalação (datas da OS)')
    expect(document.querySelector('[data-marker="Prometido 10/11"]')).not.toBeNull()
  })

  it('cada barra é focável; setas navegam no tempo e entre linhas; Enter abre', () => {
    const onActivate = draw()
    const bars = screen.getAllByRole('button', { name: /^Barra / })
    expect(bars.filter((b) => b.getAttribute('tabindex') === '0')).toHaveLength(1)
    const a = screen.getByRole('button', { name: 'Barra a' })
    act(() => a.focus())
    expect(screen.getByRole('tooltip').textContent).toContain('Detalhe a')
    fireEvent.keyDown(a, { key: 'ArrowRight' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Barra b' }))
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Barra c' }))
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Barra d' }))
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowUp' })
    fireEvent.keyDown(document.activeElement!, { key: 'Enter' })
    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ id: 'c' }), expect.objectContaining({ id: 'r2' }))
  })

  it('a barra inerte não abre nada', () => {
    const onActivate = draw(vi.fn(), [{ id: 'p', label: 'Projeto', depth: 0, bars: [bar('p', '2026-11-02', '2026-11-30', { kind: 'summary', inert: true })] }])
    const p = screen.getByRole('button', { name: 'Barra p' })
    fireEvent.keyDown(p, { key: 'Enter' })
    fireEvent.click(p)
    expect(onActivate).not.toHaveBeenCalled()
  })

  it('virtualiza as linhas: com 1.500 linhas desenha só a janela visível e a margem', () => {
    const many: GanttRow[] = Array.from({ length: 1500 }, (_, i) => ({ id: `r${i}`, label: `Linha ${i}`, depth: 0, bars: [bar(`b${i}`, '2026-11-02', '2026-11-05')] }))
    draw(vi.fn(), many)
    const drawn = document.querySelectorAll('[data-bar]').length
    expect(drawn).toBeGreaterThan(0)
    expect(drawn).toBeLessThan(60)
  })
})
