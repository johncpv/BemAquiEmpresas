import { type Cents, toCents } from '@/pdv/money'
import type { SupervisorAction } from './supervisor'


/** Percentual com até 2 casas, de 0 a 100 (vírgula ou ponto); null quando não serve. */
export function parsePercent(text: string): number | null {
  const match = /^(\d{1,3})(?:[.,](\d{1,2}))?$/.exec(text.trim())
  if (!match) return null
  const value = Number(`${match[1]}.${match[2] ?? '0'}`)
  return value <= 100 ? value : null
}

/** Valor em R$ digitado (vírgula ou ponto, até 2 casas) em centavos; null quando não serve. */
export function parseMoney(text: string): Cents | null {
  try {
    const cents = toCents(text.replace(/\./g, '').replace(',', '.').trim() || 'x')
    return cents >= 0 ? cents : null
  } catch {
    return null
  }
}

/** Motivos de cada ação (PDV-04 CA 1: lista com "Outro" e texto). */
export const REASONS: Record<SupervisorAction, readonly string[]> = {
  Discount: ['Cliente fiel', 'Avaria no produto', 'Negociação', 'Erro na etiqueta'],
  ChangePrice: ['Erro na etiqueta', 'Preço combinado', 'Avaria no produto'],
  CancelItem: ['Cliente desistiu', 'Item lançado errado', 'Avaria no produto'],
  CancelSale: ['Cliente desistiu', 'Erro no lançamento', 'Pagamento errado'],
}
