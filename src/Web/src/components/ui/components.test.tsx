import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { formatDate, formatMoney } from '@/lib/format'
import { Money } from './money'
import { DEFAULT_STAGES } from '@/lib/stages'
import { StageIndicator } from './stage-indicator'
import { StatusChip } from './status-chip'

afterEach(cleanup)

const nbsp = (s: string | null) => (s ?? '').replace(/ /g, ' ')

describe('Money (FND-09a CA 15)', () => {
  it('formata em reais com e sem centavos', () => {
    expect(nbsp(formatMoney(1234.5))).toBe('R$ 1.234,50')
    expect(nbsp(formatMoney(1234.5, false))).toBe('R$ 1.235')
  })

  it('usa tabular-nums e destaca negativo quando pedido', () => {
    const { container } = render(<Money value={-10} highlightNegative />)
    const span = container.querySelector('span')!
    expect(span.className).toContain('tabular-nums')
    expect(span.className).toContain('text-crit-ink')
    expect(nbsp(span.textContent)).toBe('-R$ 10,00')
  })
})

describe('StatusChip (CA 13)', () => {
  it.each(['ok', 'warn', 'crit', 'info'] as const)('%s usa os tokens soft/ink e tem rótulo de texto', (tone) => {
    render(<StatusChip tone={tone}>Rótulo {tone}</StatusChip>)
    const chip = screen.getByText(`Rótulo ${tone}`)
    expect(chip.className).toContain(`bg-${tone}-soft`)
    expect(chip.className).toContain(`text-${tone}-ink`)
  })
})

describe('StageIndicator (CA 14)', () => {
  it('mostra a letra da etapa e o rótulo acessível', () => {
    render(<StageIndicator stages={DEFAULT_STAGES} current={2} />)
    const indicator = screen.getByRole('img', { name: 'Etapa Montagem, 3 de 5' })
    expect(indicator.textContent).toBe('M')
  })

  it('só a atual fica em âmbar; as concluídas apagadas e as futuras na linha (ENG-01 CA 14, D17 b)', () => {
    const { container } = render(<StageIndicator stages={DEFAULT_STAGES} current={2} />)
    const segments = [...container.querySelectorAll('[data-state]')]
    expect(segments.map((s) => s.getAttribute('data-state'))).toEqual(['done', 'done', 'now', 'next', 'next'])
    expect(segments.filter((s) => s.className.includes('bg-signal'))).toHaveLength(1)
    expect(segments[0].className).toContain('bg-muted-foreground')
    expect(segments[4].className).toContain('bg-border')
  })

  it('a letra vem do roteiro; a inicial do nome é só a reserva', () => {
    render(<StageIndicator stages={['Projeto', 'Corte', 'Expedição']} letters={['P', null, 'X']} current={2} />)
    expect(screen.getByRole('img', { name: 'Etapa Expedição, 3 de 3' }).textContent).toBe('X')
    cleanup()
    render(<StageIndicator stages={['Projeto', 'corte']} letters={['P']} current={1} />)
    expect(screen.getByRole('img', { name: 'Etapa corte, 2 de 2' }).textContent).toBe('C')
  })

  it('nomes repetidos não quebram (key pelo índice)', () => {
    const { container } = render(<StageIndicator stages={['Solda', 'Solda', 'Solda']} current={1} />)
    expect(container.querySelectorAll('[data-state]')).toHaveLength(3)
  })

  it('acima de 8 etapas usa o formato compacto "M · 3/12" sem segmentos', () => {
    const stages = Array.from({ length: 12 }, (_, i) => `Etapa ${i + 1}`)
    const letters = Array.from({ length: 12 }, (_, i) => String.fromCharCode(65 + i))
    const { container } = render(<StageIndicator stages={stages} letters={letters} current={2} />)
    const indicator = screen.getByRole('img', { name: 'Etapa Etapa 3, 3 de 12' })
    expect(nbsp(indicator.textContent)).toBe('C· 3/12')
    expect(container.querySelectorAll('[data-state]')).toHaveLength(0)
    cleanup()
    const { container: eight } = render(<StageIndicator stages={stages.slice(0, 8)} current={0} />)
    expect(eight.querySelectorAll('[data-state]')).toHaveLength(8)
  })

  it('30 etapas cabem a 400 px: o compacto tem largura fixa, sem um segmento por etapa', () => {
    const stages = Array.from({ length: 30 }, (_, i) => `Etapa ${i + 1}`)
    const { container } = render(<StageIndicator stages={stages} current={29} showLabel />)
    expect(screen.getByRole('img', { name: 'Etapa Etapa 30, 30 de 30' })).toBeTruthy()
    expect(container.querySelector('[data-compact]')?.textContent).toBe('· 30/30')
    expect(container.querySelector('[role="img"]')?.className).toContain('max-w-full')
  })
})

describe('datas', () => {
  it('data de negócio não muda de dia por fuso', () => {
    expect(formatDate('2026-10-01')).toBe('01/10/2026')
  })
})
