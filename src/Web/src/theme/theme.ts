/** Preferência de tema (FND-09a): "Automático" segue o sistema; é o padrão (propositivo). */
export type ThemePreference = 'system' | 'light' | 'dark'

export const THEME_STORAGE_KEY = 'bq-theme'
export const THEMES: ReadonlyArray<{ value: ThemePreference; label: string }> = [
  { value: 'system', label: 'Auto' },
  { value: 'light', label: 'Claro' },
  { value: 'dark', label: 'Escuro' },
]

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark'
}

export function readStoredTheme(): ThemePreference {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    return isThemePreference(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

/** Aplica no <html>: "system" remove o atributo e deixa o CSS seguir prefers-color-scheme (CA 10, sem recarregar). */
export function applyTheme(preference: ThemePreference, root: HTMLElement = document.documentElement): void {
  if (preference === 'system') {
    root.removeAttribute('data-theme')
  } else {
    root.setAttribute('data-theme', preference)
  }

  try {
    localStorage.setItem(THEME_STORAGE_KEY, preference)
  } catch {
    // armazenamento bloqueado: o tema vale só nesta sessão
  }
}
