export const PROJECT_DICAS = {
  margemProjetada: 'Receita contratada menos o custo projetado (realizado mais o que falta do orçado), sobre a receita.',
  realizado: 'Custo já lançado no projeto: por enquanto, os títulos a pagar com o projeto. Material e horas entram com a produção (Fase 3).',
  projetado: 'Realizado mais o que ainda falta do orçado. Se o realizado passou do orçado, é o próprio realizado.',
  desvio: 'Projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.',
}

export const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Dias de atraso da entrega prometida (COM-05): só enquanto o projeto não foi entregue. */
export function delayDays(promisedDate: string | null | undefined, status: string | null | undefined, today: string) {
  if (!promisedDate || status === 'Delivered' || status === 'Closed' || status === 'Cancelled' || promisedDate >= today) return null
  return Math.round((Date.parse(today + 'T00:00:00Z') - Date.parse(promisedDate + 'T00:00:00Z')) / 86_400_000)
}
