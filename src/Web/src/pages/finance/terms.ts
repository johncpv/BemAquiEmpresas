import type { Installment } from '@/api/generated/model'
import { useNavigation } from '@/navigation/navigation-context'

/** Linha do editor de parcelas: dias e percentual como texto normalizado ("33.3333"), como o DecimalInput entrega. */
export interface InstallmentRow { days: string; percent: string | null }

/** Percentual em décimos de milésimo (inteiro), para somar sem erro de ponto flutuante. */
const units = (percent: string | null) => Math.round(Number(percent ?? 0) * 10000)

export const percentSum = (rows: InstallmentRow[]) => rows.reduce((s, r) => s + units(r.percent), 0) / 10000

/** "Dividir igualmente" (CA 13): mesma regra do servidor, 33,3333 + 33,3333 + 33,3334. */
export function splitEqually(rows: InstallmentRow[]): InstallmentRow[] {
  const each = Math.floor(1_000_000 / rows.length)
  return rows.map((r, i) => ({ ...r, percent: ((i === rows.length - 1 ? 1_000_000 - each * (rows.length - 1) : each) / 10000).toFixed(4) }))
}

export const toInstallments = (rows: InstallmentRow[]): Installment[] => rows.map((r) => ({ days: Number(r.days || 0), percent: Number(r.percent ?? 0) }))

export const toRows = (installments: Installment[]): InstallmentRow[] => installments.map((i) => ({ days: String(i.days), percent: i.percent.toFixed(4) }))

/** "30, 60 e 90 dias", "À vista" para dia 0. */
export function describeDays(installments: Installment[]): string {
  const days = installments.map((i) => (i.days === 0 ? 'à vista' : String(i.days)))
  if (days.length === 1) return installments[0].days === 0 ? 'À vista' : `${days[0]} dias`
  return `${days.slice(0, -1).join(', ')} e ${days.at(-1)} dias`
}

/** Ação do módulo finance liberada para o usuário (o mesmo critério do <Can>). */
export function useCanFinance(action: string) {
  const { all } = useNavigation()
  const module = all.find((m) => m.code === 'finance')
  return !!module && !module.locked && module.actions.includes(action)
}
