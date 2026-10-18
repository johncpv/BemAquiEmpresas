/** Telas usadas por último: a paleta mostra primeiro (propositivo, FND-09b). */
const RECENT_KEY = 'bq-recent'

export function readRecent(): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]')
    return Array.isArray(parsed) ? parsed.filter((p): p is string => typeof p === 'string') : []
  } catch {
    return []
  }
}

export function rememberRecent(path: string) {
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify([path, ...readRecent().filter((p) => p !== path)].slice(0, 5)))
  } catch {
    // sem armazenamento: só não lembra os recentes
  }
}
