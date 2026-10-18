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
})

describe('datas', () => {
  it('data de negócio não muda de dia por fuso', () => {
    expect(formatDate('2026-10-01')).toBe('01/10/2026')
  })
})
