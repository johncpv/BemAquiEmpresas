import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ProductionBoard } from '@/api/generated/model'
import { renderWithRouter } from '@/test-utils'
import { KanbanPage } from './KanbanPage'
import { FULLSCREEN_KEY } from './useBoardFullscreen'

const board: ProductionBoard = {
  columns: [
    { key: 'P', kind: 'Step', code: 'P', name: 'Projeto', count: 0, remainingHours: 0 },
    { key: 'C', kind: 'Step', code: 'C', name: 'Corte', count: 1, remainingHours: 2 },
  ],
  cards: [{
    id: 'of1', number: 'OS-2026-00007', status: 'InProgress', productName: 'Elevador 6 paradas', quantity: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0003',
    projectName: 'Aurora', partyId: 'cli1', partyName: 'Condomínio Aurora', columnKey: 'C', currentStepId: 's2', currentStepCode: 'C', currentStepName: 'Corte',
    currentStepStatus: 'Ready', currentStepIsQuality: false, standardHours: 2, appliedHours: 0, line: 'Linha 1', codes: ['P', 'C'],
    stepNames: ['Projeto', 'Corte'], currentStepIndex: 1, promisedDate: '2026-12-25', risk: 'None', delayDays: null, readyToComplete: false,
    noRouting: false, inExecution: false, pausedReason: null, operatorInitials: null, routingTemplateId: 'rt1', version: 3,
  }],
  lateCount: 0, atRiskCount: 0, openCount: 1, projects: [], parties: [], routings: [], lines: [],
}

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetProductionBoard: () => ({ data: board, isPending: false, isError: false, refetch: vi.fn() }),
}))
vi.mock('./useBoardLive', async (original) => ({ ...(await original<typeof import('./useBoardLive')>()), useBoardLive: () => 'live' }))
vi.mock('../planning/use-forecasts', async (original) => ({ ...(await original<typeof import('../planning/use-forecasts')>()), useOrderForecasts: () => new Map() }))
vi.mock('@/pages/use-can', () => ({ useCan: () => false }))
vi.mock('@/shell/PageHeading', () => ({ PageHeading: ({ title, children }: { title: string; children?: ReactNode }) => <div><h1>{title}</h1>{children}</div> }))

function renderPage() {
  const client = new QueryClient()
  return renderWithRouter(() => <QueryClientProvider client={client}><KanbanPage /></QueryClientProvider>)
}

const root = document.documentElement as unknown as { requestFullscreen?: () => Promise<void> }
let fullscreenElement: Element | null = null

beforeEach(() => {
  fullscreenElement = null
  Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreenElement })
  sessionStorage.clear()
})

afterEach(() => {
  cleanup()
  delete root.requestFullscreen
  delete (document as { exitFullscreen?: unknown }).exitFullscreen
  sessionStorage.clear()
})

describe('Kanban em tela cheia', () => {
  it('liga pela Fullscreen API, esconde os filtros, ocupa a tela e sai pelo Esc nativo', async () => {
    const request = vi.fn(() => { fullscreenElement = document.documentElement; return Promise.resolve() })
    const exit = vi.fn(() => { fullscreenElement = null; return Promise.resolve() })
    root.requestFullscreen = request
    ;(document as { exitFullscreen?: unknown }).exitFullscreen = exit
    renderPage()

    const toggle = await screen.findByRole('button', { name: 'Tela cheia' })
    expect(toggle.getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByRole('button', { name: 'Ver como lista' })).toBeTruthy()
    fireEvent.click(toggle)

    expect(request).toHaveBeenCalledTimes(1)
    const quadro = screen.getByTestId('quadro')
    expect(quadro.className).toContain('fixed')
    expect(quadro.className).toContain('inset-0')
    expect(quadro.className).toContain('bg-background')
    expect(screen.queryByRole('button', { name: 'Ver como lista' })).toBeNull()
    const exitButton = screen.getByRole('button', { name: 'Sair da tela cheia' })
    expect(exitButton.getAttribute('aria-pressed')).toBe('true')
    expect(exitButton).toBe(toggle)
    expect(sessionStorage.getItem(FULLSCREEN_KEY)).toBe('1')
    expect(screen.getByRole('list', { name: /Corte: 1 OS/ })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'OS-2026-00007' })).toBeTruthy()

    // Esc nativo: o navegador sai da tela cheia e avisa; o quadro volta ao normal.
    fullscreenElement = null
    act(() => { document.dispatchEvent(new Event('fullscreenchange')) })
    expect(screen.getByRole('button', { name: 'Tela cheia' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByTestId('quadro').className).not.toContain('fixed')
    expect(sessionStorage.getItem(FULLSCREEN_KEY)).toBeNull()
  })

  it('sem a API vira a camada fixa em CSS, sai com Esc e lembra a escolha na sessão', async () => {
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Tela cheia' }))
    expect(screen.getByTestId('quadro').className).toContain('fixed')
    expect(sessionStorage.getItem(FULLSCREEN_KEY)).toBe('1')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.getByTestId('quadro').className).not.toContain('fixed')
    cleanup()

    sessionStorage.setItem(FULLSCREEN_KEY, '1')
    renderPage()
    expect((await screen.findByRole('button', { name: 'Sair da tela cheia' })).getAttribute('aria-pressed')).toBe('true')
  })

  it('sessionStorage bloqueado não quebra a tela', async () => {
    const spy = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado') })
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado') })
    try {
      renderPage()
      fireEvent.click(await screen.findByRole('button', { name: 'Tela cheia' }))
      expect(screen.getByRole('button', { name: 'Sair da tela cheia' })).toBeTruthy()
    } finally {
      spy.mockRestore()
      set.mockRestore()
    }
  })
})
