import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from './App'
import { ThemeProvider } from './theme/ThemeProvider'

describe('App', () => {
  it('mostra o nome do produto', () => {
    render(
      <ThemeProvider>
        <App />
      </ThemeProvider>,
    )
    expect(screen.getByRole('heading', { name: 'BemAqui Empresas' })).toBeDefined()
  })
})
