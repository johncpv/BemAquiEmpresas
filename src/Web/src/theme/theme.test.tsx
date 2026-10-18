import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyTheme, readStoredTheme, THEME_STORAGE_KEY } from './theme'
import { useTheme } from './theme-context'
import { ThemeProvider } from './ThemeProvider'

function Probe() {
  const { theme, setTheme } = useTheme()
  return (
    <button type="button" onClick={() => setTheme('dark')}>
      {theme}
    </button>
  )
}

describe('tema (FND-09a CA 8 a 10)', () => {
  afterEach(cleanup)

  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
  })

  it('Automático é o padrão e não força atributo (o CSS segue o sistema)', () => {
    expect(readStoredTheme()).toBe('system')
    applyTheme('system')
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })

  it('escolha é guardada localmente, aplicada no html e enviada ao perfil', () => {
    const persist = vi.fn()
    render(
      <ThemeProvider onPersist={persist}>
        <Probe />
      </ThemeProvider>,
    )
    act(() => screen.getByRole('button').click())
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(persist).toHaveBeenCalledWith('dark')
  })

  it('tema do perfil vence o guardado no navegador', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark')
    render(
      <ThemeProvider profileTheme="light">
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByRole('button').textContent).toBe('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })
})
