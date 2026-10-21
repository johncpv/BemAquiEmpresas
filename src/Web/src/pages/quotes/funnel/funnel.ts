import type { QuoteLostReason } from '@/api/generated/model'

export const LOST_REASONS: Record<NonNullable<QuoteLostReason>, string> = {
  Price: 'Preço',
  Deadline: 'Prazo',
  Competitor: 'Concorrente',
  CustomerGaveUp: 'Desistência do cliente',
  NoAnswer: 'Sem retorno',
  Other: 'Outro',
}

export const FUNNEL_DICAS = {
  conversao: 'Aprovados ÷ decididos (aprovados + perdidos + expirados). Rascunhos e enviados ainda não foram decididos e ficam fora da conta.',
  periodo: 'Conta pela data em que o orçamento nasceu (revisão 0). Um orçamento revisado entra uma vez, na situação da revisão mais recente.',
}

/** "33,3%" ou "—" quando não há orçamento decidido (CA 2). */
export function pctLabel(value: number | null | undefined) {
  return value === null || value === undefined ? '—' : `${value.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
}

/** Largura da barra em %, proporcional ao maior valor; mínimo visível de 2% quando há valor. */
export function barWidth(value: number, max: number) {
  if (max <= 0 || value <= 0) return 0
  return Math.max(2, Math.round((value / max) * 100))
}

/** Soma dias a uma data ISO (yyyy-mm-dd), sem fuso. */
export function addDays(iso: string, days: number) {
  const d = new Date(iso + 'T00:00:00Z')
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}
