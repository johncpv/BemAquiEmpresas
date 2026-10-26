import { act, render, renderHook, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { KioskBoard, KioskCard } from '@/api/generated/model'
import { TvCard, TvColumn, TvViewIndicator } from '@/components/tv/panel'
import { columnCards, columnPage, columnsPerPage, elapsedText, headline, hoursText, operatorLine, PANEL_TEXT, patchBoard, rotationViews, sortCards } from './panel'
import { useRotation } from './useRotation'

const card = (over: Partial<KioskCard> = {}): KioskCard => ({
  id: 'o1', number: 'OS-2026-0001', projectNumber: 'PRJ-2026-0007', projectName: 'Residencial Aurora', columnKey: 'M', stepCode: 'M', stepName: 'Montagem',
  operator: 'João S.', pausedReason: null, stepStartedAt: '2026-10-26T12:00:00Z', appliedHours: 1.5, standardHours: 3, plannedEnd: '2026-10-28', risk: 'None', delayDays: null, ...over,
})

const board = (cards: KioskCard[]): KioskBoard => ({
  line: 'Linha 1', inProgressCount: cards.length, lateCount: cards.filter((c) => c.risk === 'Late').length, atRiskCount: 0,
  columns: [{ key: 'C', code: 'C', name: 'Corte', count: cards.filter((c) => c.columnKey === 'C').length }, { key: 'M', code: 'M', name: 'Montagem', count: cards.filter((c) => c.columnKey === 'M').length }],
  cards, serverNow: '2026-10-26T13:00:00Z',
})

describe('cartão de 4 linhas (PNL-01b CA 2 e 4)', () => {
  const now = new Date('2026-10-26T13:35:00Z')

  it('número e projeto, operador, tempo na etapa com as horas e o previsto', () => {
    const { container } = render(<TvCard card={card()} now={now} />)
    const lines = container.querySelectorAll('[data-line]')
    expect(lines).toHaveLength(4)
    expect(lines[0].textContent).toBe('OS-2026-0001Residencial Aurora')
    expect(lines[1].textContent).toBe('João S.')
    expect(lines[2].textContent).toContain('1:35')
    expect(lines[2].textContent).toContain('1,5 de 3 h')
    expect(lines[3].textContent).toBe('Previsto 28/10')
    expect(container.querySelector('article')!.className).toContain('--tv-line')
    expect(container.textContent).not.toContain('Elevador')
  })

  it('atrasada com a faixa vermelha, em risco com a faixa laranja e horas acima do padrão em crítico', () => {
    const late = render(<TvCard card={card({ risk: 'Late', delayDays: 3, appliedHours: 4 })} now={now} />)
    expect(late.getByText(PANEL_TEXT.late).className).toContain('text-crit-ink')
    expect(late.container.querySelector('article')!.className).toContain('border-l-crit')
    expect(late.getByText('4 de 3 h').className).toContain('text-crit-ink')
    late.unmount()

    const risk = render(<TvCard card={card({ risk: 'AtRisk' })} now={now} />)
    expect(risk.getByText(PANEL_TEXT.atRisk)).toBeTruthy()
    expect(risk.container.querySelector('article')!.className).toContain('border-l-warn')
  })

  it('etapa pausada mostra o motivo no lugar do operador; etapa não iniciada não mostra o tempo', () => {
    const { container } = render(<TvCard card={card({ operator: null, pausedReason: 'Falta de material', stepStartedAt: null, plannedEnd: null })} now={now} />)
    const lines = container.querySelectorAll('[data-line]')
    expect(lines[1].textContent).toBe('Pausada: Falta de material')
    expect(screen.queryByLabelText('Tempo na etapa')).toBeNull()
    expect(lines[3].textContent).toBe(PANEL_TEXT.noForecast)
    expect(operatorLine({ operator: null, pausedReason: null })).toBe('Sem operador')
  })

  it('tempo na etapa em h:mm e horas em pt-BR', () => {
    expect(elapsedText('2026-10-26T12:00:00Z', new Date('2026-10-26T12:07:59Z'))).toBe('0:07')
    expect(elapsedText('2026-10-26T08:00:00Z', new Date('2026-10-26T18:05:00Z'))).toBe('10:05')
    expect(hoursText(0, 2)).toBe('0 de 2 h')
  })
})

describe('coluna com até 3 cartões (PNL-01b CA 1 e 3)', () => {
  it('atrasadas, depois em risco, depois pelo fim previsto, e "+{n} OS"', () => {
    const cards = [
      card({ id: 'a', number: 'OS-A', plannedEnd: '2026-10-30' }), card({ id: 'b', number: 'OS-B', risk: 'AtRisk' }), card({ id: 'c', number: 'OS-C', risk: 'Late', delayDays: 1 }),
      card({ id: 'd', number: 'OS-D', plannedEnd: '2026-10-27' }), card({ id: 'e', number: 'OS-E', risk: 'Late', delayDays: 5 }), card({ id: 'x', number: 'OS-X', columnKey: 'C', plannedEnd: '2026-11-05' }),
    ]
    expect(sortCards(cards).map((c) => c.number)).toEqual(['OS-E', 'OS-C', 'OS-B', 'OS-D', 'OS-A', 'OS-X'])
    const { shown, more } = columnCards(cards, 'M')
    expect(shown.map((c) => c.number)).toEqual(['OS-E', 'OS-C', 'OS-B'])
    expect(more).toBe(2)

    const { container } = render(<TvColumn column={{ key: 'M', code: 'M', name: 'Montagem', count: 5 }} cards={cards} now={new Date('2026-10-26T13:00:00Z')} />)
    expect(container.querySelectorAll('[data-tv-card]')).toHaveLength(3)
    expect(screen.getByText('+2 OS')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Montagem' }).className).toContain('--tv-title')
    expect(screen.getByText('5').className).toContain('--tv-count')
    expect(container.querySelector('[data-size="tv"]')).toBeTruthy()
  })

  it('cabeçalho com a linha, em andamento e atrasadas', () => {
    expect(headline('Linha 1', { inProgressCount: 9, lateCount: 2 })).toBe('Linha 1 — 9 OS em andamento, 2 atrasadas')
    expect(headline(null, { inProgressCount: 1, lateCount: 1 })).toBe('Todas as linhas — 1 OS em andamento, 1 atrasada')
    expect(headline('Linha 1', undefined)).toBe('Linha 1')
  })

  it('5 colunas a 1920 px e 3 a 1366 px, rolando as colunas a cada rotação', () => {
    expect(columnsPerPage(1920 - 64)).toBe(5)
    expect(columnsPerPage(1366 - 64)).toBe(3)
    expect(columnsPerPage(300)).toBe(1)
    const columns = ['P', 'C', 'M', 'S', 'Q']
    expect(columnPage(columns, 3, 0)).toEqual({ items: ['P', 'C', 'M'], page: 0, pages: 2 })
    expect(columnPage(columns, 3, 1)).toEqual({ items: ['S', 'Q'], page: 1, pages: 2 })
    expect(columnPage(columns, 3, 2).page).toBe(0)
    expect(columnPage(columns, 5, 7)).toEqual({ items: columns, page: 0, pages: 1 })
  })
})

describe('recarga por cartão (PNL-01b CA 8 e 9)', () => {
  it('troca o cartão, muda de coluna e recalcula as contagens sem recarregar o quadro', () => {
    const before = board([card({ id: 'a', columnKey: 'C', stepCode: 'C' }), card({ id: 'b' })])
    const moved = patchBoard(before, 'a', card({ id: 'a', columnKey: 'M', operator: 'Ana L.', risk: 'Late', delayDays: 2 }))!
    expect(moved.cards.map((c) => c.id)).toEqual(['a', 'b'])
    expect(moved.columns.map((c) => [c.key, c.count])).toEqual([['M', 2]])
    expect(moved.lateCount).toBe(1)
    expect(moved.inProgressCount).toBe(2)
  })

  it('OS que saiu da linha (404) sai do quadro; coluna nova pede a recarga do quadro', () => {
    const before = board([card({ id: 'a' }), card({ id: 'b' })])
    expect(patchBoard(before, 'a', null)!.cards.map((c) => c.id)).toEqual(['b'])
    expect(patchBoard(before, 'a', card({ id: 'a', columnKey: 'Q' }))).toBeNull()
    expect(patchBoard(before, 'z', card({ id: 'z', columnKey: 'C' }))!.columns.map((c) => [c.key, c.count])).toEqual([['C', 1], ['M', 2]])
  })
})

describe('rotação (PNL-01b CA 6 e 7)', () => {
  afterEach(() => vi.useRealTimers())

  it('as visões da tela, na ordem do painel, pulando Atrasos e riscos vazia', () => {
    expect(rotationViews(['late', 'board', 'queue'], undefined)).toEqual(['board', 'queue', 'late'])
    expect(rotationViews(['board', 'queue', 'late'], { total: 0 })).toEqual(['board', 'queue'])
    expect(rotationViews(['board', 'late'], { total: 2 })).toEqual(['board', 'late'])
    expect(rotationViews(['late'], { total: 0 })).toEqual([])
  })

  it('troca no intervalo, volta ao começo e não troca com uma visão só', () => {
    vi.useFakeTimers()
    const three = renderHook(() => useRotation(3, 20, false))
    expect(three.result.current.index).toBe(0)
    act(() => vi.advanceTimersByTime(20_000))
    expect(three.result.current.index).toBe(1)
    act(() => vi.advanceTimersByTime(40_000))
    expect(three.result.current.index).toBe(0)
    act(() => three.result.current.previous())
    expect(three.result.current.index).toBe(2)

    const one = renderHook(() => useRotation(1, 10, false))
    act(() => vi.advanceTimersByTime(30_000))
    expect(one.result.current.index).toBe(0)
    expect(one.result.current.tick).toBe(3)

    const paused = renderHook(() => useRotation(3, 10, true))
    act(() => vi.advanceTimersByTime(30_000))
    expect(paused.result.current.index).toBe(0)
  })

  it('indicador discreto da visão atual', () => {
    render(<TvViewIndicator view="queue" index={1} count={3} />)
    expect(screen.getByLabelText('Visão 2 de 3: Fila e previsões').textContent).toBe('Fila e previsões2/3')
  })
})
