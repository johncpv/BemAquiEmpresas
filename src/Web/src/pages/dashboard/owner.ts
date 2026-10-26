import type { ActiveProjectItem, ProductionWeekItem, ProjectResultLine } from '@/api/generated/model'
import { formatDate } from '@/lib/format'
import { monthLabel, monthLong } from '@/pages/finance/dre/dre'

/** Lista de projetos em andamento (drill-down de Projetos, Carteira e Entregas). */
export const PROJECTS_ORIGIN = '/app/projetos?situacao=andamento'

/** Resultado gerencial por projeto (DSH-01b CA 6). */
export const RESULTS_ROUTE = '/app/painel/resultado-por-projeto'

/** Drill-down da margem realizada: o Orçado × realizado com os entregues e encerrados no ano. */
export function finishedMarginHref(year: number) {
  return { to: '/app/projetos/margens', search: { situacao: 'concluido', entregueDe: `${year}-01-01`, entregueAte: `${year}-12-31` } }
}

/** Entrega na tabela (CA 1): "Atrasado n dias" (crítico), "+n dias" (risco, previsão depois do prometido) ou "No prazo". */
export function deliveryOf(item: Pick<ActiveProjectItem, 'risk' | 'delayDays' | 'slackDays' | 'forecastDate'>): { label: string; tone?: 'crit' | 'warn' } {
  if (item.risk === 'Late') return { label: `Atrasado ${item.delayDays ?? 0} ${item.delayDays === 1 ? 'dia' : 'dias'}`, tone: 'crit' }
  if (item.risk === 'AtRisk') {
    const days = Math.abs(item.slackDays ?? 0)
    return { label: `+${days} ${days === 1 ? 'dia' : 'dias'}`, tone: 'warn' }
  }
  return { label: item.forecastDate ? `No prazo (previsão ${formatDate(item.forecastDate)})` : 'No prazo' }
}

/** Pontos do gráfico de ocupação: "S44 · 26/10" e o % da semana (sem capacidade, sem valor). */
export function occupancyPoints(weeks: readonly ProductionWeekItem[]) {
  return weeks.map((w) => ({ semana: `S${w.isoWeek} · ${formatDate(w.start).slice(0, 5)}`, ocupacao: typeof w.percent === 'number' ? w.percent : undefined }))
}

export { monthLabel, monthLong }

/** "aaaa-mm" de hoje e de n meses antes (os 12 meses padrão). */
export function defaultPeriod(today = new Date()) {
  const to = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
  const start = new Date(today.getFullYear(), today.getMonth() - 11, 1)
  const from = `${start.getFullYear()}-${String(start.getMonth() + 1).padStart(2, '0')}`
  return { from, to }
}

/** Pontos do gráfico "Receita × custos diretos por mês" (CA 7). */
export function resultPoints(months: readonly { month: string }[], lines: readonly ProjectResultLine[]) {
  const revenue = lines.find((l) => l.key === 'revenue')
  const costs = lines.find((l) => l.key === 'costs')
  return months.map((m, i) => ({ mes: monthLabel(m.month), receita: revenue?.values[i] ?? 0, custos: costs?.values[i] ?? 0 }))
}

/** Resumo textual do gráfico: o total do período e o pior mês. */
export function resultSummary(months: readonly { month: string }[], lines: readonly ProjectResultLine[], money: (v: number) => string) {
  const result = lines.find((l) => l.key === 'result')
  if (!result) return ''
  let worst = -1
  result.values.forEach((v, i) => { if (v < 0 && (worst < 0 || v < result.values[worst])) worst = i })
  const total = `Resultado do período: ${money(result.total)}.`
  return worst >= 0 ? `${total} O pior mês foi ${monthLong(months[worst].month)}, com ${money(result.values[worst])}.` : `${total} Nenhum mês com resultado negativo.`
}

export const RESULT_DICAS = {
  note: 'Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento.',
  diferenca: 'Uma compra de material para o estoque entra no DRE no mês do documento; no projeto, o custo só entra quando o material é baixado para a OS. Por isso o resultado do projeto num mês não é a soma do DRE desse mês.',
  custosDiretos: 'Custos realizados do projeto com data no período: material baixado para as OS (menos devoluções), horas apontadas, títulos a pagar confirmados com o projeto e o imposto das notas. Não entram o comprometido, o projetado nem despesas indiretas.',
  receita: 'Receita bruta do projeto pela mesma regra do DRE: até as notas reais, a data de emissão dos títulos (provisório); o sinal entra quando o projeto expede ou no marco seguinte.',
  provisorio: 'Até a emissão de notas no sistema, a receita é a data de emissão dos títulos a receber.',
}
