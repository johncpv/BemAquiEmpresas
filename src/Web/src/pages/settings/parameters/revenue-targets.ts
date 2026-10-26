import type { RevenueTargetsYear } from '@/api/generated/model'

/** Âncora de "Definir meta" no painel (DSH-01a CA 9). */
export const TARGETS_ANCHOR = 'metas-de-faturamento'

/** Os 12 valores como texto do campo (string normalizada ou null); mês ausente fica sem meta. */
export const toInputs = (year: Partial<RevenueTargetsYear>) =>
  Array.from({ length: 12 }, (_, i) => {
    const amount = year.months?.find((m) => m.month === i + 1)?.amount
    return typeof amount === 'number' ? String(amount) : null
  })

/** "Repetir para o ano": o primeiro mês preenchido vai para os 12; sem nenhum, nada muda. */
export function repeatForYear(values: readonly (string | null)[]) {
  const first = values.find((v) => v !== null && v !== '')
  return first === undefined ? [...values] : values.map(() => first)
}

export const sumOf = (values: readonly (string | null)[]) => values.reduce((total, v) => total + (v ? Number(v) : 0), 0)
