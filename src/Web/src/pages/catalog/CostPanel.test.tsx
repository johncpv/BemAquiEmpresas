import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CostPanel } from './CostPanel'

describe('painel de custo e margem (CAT-01 CA 11)', () => {
  it('mostra custo, margem e markup e oferece o preço sugerido pela categoria', () => {
    const use = vi.fn()
    render(<CostPanel cost={{ averageCost: 6, marginPercent: 40, markupPercent: 66.67, suggestedPrice: 8.58 }} price={10} onUseSuggested={use} />)
    expect(screen.getByRole('complementary', { name: /Custo e margem/ })).toBeTruthy()
    expect(screen.getByText('40,00%')).toBeTruthy()
    expect(screen.getByText('66,67%')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Usar este preço' }))
    expect(use).toHaveBeenCalledWith(8.58)
  })

  it('avisa o preço abaixo do custo e o produto sem custo', () => {
    const { rerender } = render(<CostPanel cost={{ averageCost: 12, marginPercent: -20, markupPercent: -16.67, suggestedPrice: null }} price={10} />)
    expect(screen.getByRole('status').textContent).toBe('O preço está abaixo do custo.')
    rerender(<CostPanel cost={{ averageCost: 0, marginPercent: 100, markupPercent: null, suggestedPrice: null }} price={10} />)
    expect(screen.getByText('Sem custo ainda')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Usar este preço' })).toBeNull()
  })
})
