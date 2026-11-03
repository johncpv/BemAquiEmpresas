import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FormLayout } from './form-layout'

describe('FormLayout (ADR 0044)', () => {
  it('sem painel: só a coluna de 896 px, à esquerda', () => {
    const { container } = render(<FormLayout><form aria-label="Modelo" /></FormLayout>)
    expect(container.firstElementChild!.className).toBe('max-w-4xl')
    expect(container.firstElementChild!.className).not.toContain('mx-auto')
  })

  it('com painel: coluna + 320 px a partir de 1280 px, e o painel depois do formulário no DOM', () => {
    const { container } = render(<FormLayout asideLabel="Logo da empresa" aside={<p>Logo</p>}><form aria-label="Empresa" /></FormLayout>)
    const grid = container.firstElementChild!
    expect(grid.className).toContain('xl:grid-cols-[minmax(0,56rem)_20rem]')
    const aside = screen.getByRole('complementary', { name: 'Logo da empresa' })
    expect(aside.className).toContain('xl:sticky')
    expect(grid.lastElementChild).toBe(aside)
    expect(screen.getByRole('form', { name: 'Empresa' }).compareDocumentPosition(aside) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
