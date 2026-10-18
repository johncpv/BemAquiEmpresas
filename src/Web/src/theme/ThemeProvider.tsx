import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { applyTheme, readStoredTheme, type ThemePreference } from './theme'
import { ThemeContext } from './theme-context'

interface ThemeProviderProps {
  children: ReactNode
  /** Tema salvo no perfil (FND-09a CA 9): quando chega do servidor, vence o guardado no navegador. */
  profileTheme?: ThemePreference | null
  /** Grava a escolha no perfil (PUT /api/v1/me/preferences, pelo cliente gerado na FND-09c). */
  onPersist?: (theme: ThemePreference) => void
}

/** Tema escolhido pelo usuário, aplicado no <html> e lembrado localmente (sem flash) e no perfil. */
export function ThemeProvider({ children, profileTheme, onPersist }: ThemeProviderProps) {
  const [stored] = useState(readStoredTheme)
  const [chosen, setChosen] = useState<ThemePreference | null>(null)
  const theme = chosen ?? profileTheme ?? stored

  useEffect(() => applyTheme(theme), [theme])

  const setTheme = useCallback(
    (next: ThemePreference) => {
      setChosen(next)
      onPersist?.(next)
    },
    [onPersist],
  )

  const value = useMemo(() => ({ theme, setTheme }), [theme, setTheme])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
