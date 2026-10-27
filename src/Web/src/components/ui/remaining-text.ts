/** Tempo que falta na contagem de prazo do ConfirmDialog (D18): "3 h 12 min", "45 min", "menos de 1 min". */
export function remainingText(ms: number): string {
  if (ms < 60_000) return 'menos de 1 min'
  const minutes = Math.floor(ms / 60_000)
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return h > 0 ? `${h} h ${m} min` : `${m} min`
}
