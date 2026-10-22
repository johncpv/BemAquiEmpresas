import { cleanup, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BoardCard as Card } from '@/api/generated/model'
import { renderWithRouter } from '@/test-utils'
import { BoardCard } from './BoardCard'
import { canCompleteStep, completeLabel, meter, stripe } from './kanban'
import { createBatcher } from './useBoardLive'

afterEach(cleanup)

const card = (over: Partial<Card> = {}): Card => ({
  id: 'of1', number: 'OS-2026-00007', status: 'InProgress', productName: 'Elevador 6 paradas', quantity: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0003',
  projectName: 'Aurora', partyId: 'cli1', partyName: 'Condomínio Aurora', columnKey: 'C', currentStepId: 's2', currentStepCode: 'C', currentStepName: 'Corte',
  currentStepStatus: 'Ready', currentStepIsQuality: false, standardHours: 2, appliedHours: 0, line: 'Linha 1', codes: ['P', 'C', 'M', 'Q'],
  stepNames: ['Projeto', 'Corte', 'Montagem', 'Qualidade'], currentStepIndex: 1, promisedDate: '2026-10-25', risk: 'None', delayDays: null, readyToComplete: false,
  noRouting: false, inExecution: false, pausedReason: null, operatorInitials: null, routingTemplateId: 'rt1', version: 3, ...over,
})

const INTERACTIVE = 'a, button, input, select, textarea, [tabindex]:not([tabindex="-1"])'

async function renderCard(c: Card, canApprove = true) {
  const view = renderWithRouter(() => <BoardCard card={c} canApprove={canApprove} onChanged={async () => undefined} />)
  await screen.findByRole('link', { name: c.number })
  return view
}

describe('Cartão do Kanban (PRD-02 CA 3, 5 e 7)', () => {
  it('não tem draggable nem interativo aninhado: o link do número e o menu são irmãos', async () => {
    const { container } = await renderCard(card({ risk: 'Late', delayDays: 3 }))
    expect(container.querySelectorAll('[draggable="true"], [ondragstart], [ondrop]')).toHaveLength(0)
    for (const element of container.querySelectorAll(INTERACTIVE)) {
      expect(element.querySelector(INTERACTIVE), `${element.tagName} tem interativo dentro`).toBeNull()
      expect(element.parentElement?.closest('a, button'), `${element.tagName} está dentro de outro interativo`).toBeNull()
    }

    const link = screen.getByRole('link', { name: 'OS-2026-00007' })
    const menu = screen.getByRole('button', { name: 'Ações da OS OS-2026-00007' })
    expect(link.parentElement).toBe(menu.parentElement)
  })

  it('faixa lateral sempre com texto: atrasada em vermelho e em risco em âmbar', async () => {
    const late = await renderCard(card({ risk: 'Late', delayDays: 3 }))
    expect(within(late.container).getByText('Atrasada 3 d').className).toContain('text-crit-ink')
    expect(late.container.querySelector('article')?.className).toContain('border-l-crit')
    cleanup()

    const risk = await renderCard(card({ risk: 'AtRisk', promisedDate: '2026-10-25' }))
    expect(within(risk.container).getByText('Em risco, previsto 25/10/2026')).toBeTruthy()
    expect(risk.container.querySelector('article')?.className).toContain('border-l-warn')
    cleanup()

    const fine = await renderCard(card())
    expect(within(fine.container).getByText('Previsto 25/10/2026')).toBeTruthy()
    expect(fine.container.querySelector('article')?.className).not.toContain('border-l-4')
  })

  it('mostra produto, projeto, cliente, etapa atual e o medidor de horas acima do padrão', async () => {
    await renderCard(card({ appliedHours: 3.5, inExecution: true }))
    expect(screen.getByText('PRJ-2026-0003 · Condomínio Aurora')).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Etapa Corte, 2 de 4' })).toBeTruthy()
    expect(screen.getByText('+1,5 h acima do padrão')).toBeTruthy()
    expect(screen.getByText('Em execução')).toBeTruthy()
  })

  it('pronta para concluir e sem roteiro levam à OS (próximo passo)', async () => {
    await renderCard(card({ readyToComplete: true, currentStepId: null, columnKey: 'pronta' }))
    expect(screen.getByText('Pronta para concluir')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Abrir a OS-2026-00007 para concluir' })).toBeTruthy()
    cleanup()

    await renderCard(card({ noRouting: true, currentStepId: null, stepNames: [], codes: [], currentStepIndex: -1, columnKey: 'sem-roteiro' }))
    expect(screen.getByRole('link', { name: 'Escolher o roteiro da OS-2026-00007' })).toBeTruthy()
  })
})

describe('Regras do Kanban no front (PRD-02)', () => {
  it('"Concluir etapa" só para a etapa atual que não é de qualidade', () => {
    expect(canCompleteStep(card())).toBe(true)
    expect(canCompleteStep(card({ currentStepIsQuality: true }))).toBe(false)
    expect(canCompleteStep(card({ readyToComplete: true }))).toBe(false)
    expect(canCompleteStep(card({ currentStepId: null }))).toBe(false)
    expect(completeLabel(card())).toBe('Concluir etapa C Corte')
  })

  it('medidor e faixa', () => {
    expect(meter({ appliedHours: 1, standardHours: 2 })).toEqual({ percent: 50, over: null })
    expect(meter({ appliedHours: 0, standardHours: 0 })).toEqual({ percent: 0, over: null })
    expect(stripe({ risk: 'None', delayDays: null, promisedDate: '2026-10-25' })).toBeNull()
  })

  it('recarga agrupada em 300 ms, sem repetir id (CA 14)', () => {
    vi.useFakeTimers()
    try {
      const flush = vi.fn()
      const batch = createBatcher(flush)
      for (let i = 0; i < 50; i++) batch.push(i % 2 === 0 ? 'a' : 'b')
      vi.advanceTimersByTime(299)
      expect(flush).not.toHaveBeenCalled()
      vi.advanceTimersByTime(1)
      expect(flush).toHaveBeenCalledTimes(1)
      expect(flush).toHaveBeenCalledWith(['a', 'b'])
      batch.push('c')
      batch.cancel()
      vi.advanceTimersByTime(500)
      expect(flush).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })
})
