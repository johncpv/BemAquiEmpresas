import type { MaintenanceCostCell, MaintenanceCostMonth, MaintenanceCostType } from '@/api/generated/model'
import { formatMoney } from '@/lib/format'

/** Custo da manutenção e paradas (PAT-03b): rótulos, dicas e regras puras das telas. */

export const COST_TYPE_LABELS: Record<MaintenanceCostType, string> = { Labor: 'Mão de obra interna', Service: 'Prestador', Parts: 'Peças', Other: 'Outros' }

/** Naturezas lançadas à mão (o prestador vem do título a pagar). */
export const MANUAL_COST_TYPES: MaintenanceCostType[] = ['Labor', 'Parts', 'Other']

export const COST_DICAS = {
  valorCombinado: 'O valor acertado com o prestador. Ele vira um título a pagar previsto, marcado como "Estimado" no fluxo de caixa, e é confirmado na conclusão da OS para seguir à ordem de pagamento.',
  horasParadas: 'Quantas horas por dia o centro de trabalho perde com a máquina em manutenção. O sistema sugere as horas do centro divididas pelas máquinas ativas dele; o planejamento desconta da capacidade.',
  duracao: 'O total de horas paradas previsto. Com 4 horas por dia e 8 horas no total, a parada ocupa 2 dias úteis.',
  comoCalculamos: 'O custo de cada OS é a soma do prestador (o título a pagar confirmado na conclusão), da mão de obra interna (horas × custo-hora do cargo de quem executou) e das peças e outros lançados na aba Custo da OS. O mês é o da conclusão, no horário de Brasília. Nenhum custo de manutenção vai para projeto.',
}

/** "2026-11" → "nov/26". */
export function monthLabel(key: string): string {
  const [y, m] = key.split('-').map(Number)
  const name = new Intl.DateTimeFormat('pt-BR', { month: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, 1))).replace('.', '')
  return `${name}/${String(y).slice(2)}`
}

/** Mês corrente e o de 11 meses antes ("aaaa-mm"), o período padrão do relatório. */
export function defaultPeriod(today = new Date()): { from: string; to: string } {
  const key = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
  return { from: key(new Date(today.getFullYear(), today.getMonth() - 11, 1)), to: key(today) }
}

/** Resumo textual acima do gráfico (padrão 1): o total, a parte da corretiva e o mês mais caro. */
export function costSummary(months: MaintenanceCostMonth[], total: MaintenanceCostCell): string {
  if (total.total <= 0) return 'Nenhum custo de manutenção no período.'
  const worst = months.reduce((a, b) => (b.cost.total > a.cost.total ? b : a), months[0])
  const corrective = Math.round((total.corrective / total.total) * 100)
  return `Custo de ${formatMoney(total.total)} no período, ${corrective}% em corretiva. O mês mais caro foi ${monthLabel(worst.month)}, com ${formatMoney(worst.cost.total)}.`
}

/** Valor curto do rótulo da coluna ("R$ 1,2 mil"). */
export const shortMoney = (v: number) => (Math.abs(v) >= 1000 ? `R$ ${(v / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mil` : formatMoney(v, false))

/** Horas por dia no formato brasileiro ("4,5 h"). */
export const formatHours = (v: number) => `${v.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} h`
