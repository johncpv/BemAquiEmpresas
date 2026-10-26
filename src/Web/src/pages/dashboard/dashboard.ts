import type { CashWeekItem } from '@/api/generated/model'
import { formatDate } from '@/lib/format'

/**
 * Ordem a 400 px (Telas e estados): Saldo → Caixa → Aguardando sua decisão → Faturamento → Vencido → Comercial. No desktop a
 * ordem é a do HTML (linha de KPIs, depois caixa e decisões, depois o comercial).
 */
export const MOBILE_ORDER: Record<string, string> = {
  balances: 'order-1',
  'cash-forecast': 'order-2',
  decisions: 'order-3',
  revenue: 'order-4',
  overdue: 'order-5',
  commercial: 'order-6',
}

/** "Como calculamos" de cada card (CA 8): a regra em uma ou duas frases. */
export const HOW = {
  balances: 'Saldo inicial de cada conta ativa mais tudo o que entrou e saiu até hoje, o mesmo número de Contas e extrato.',
  cash: 'Base: cada título no vencimento combinado. Ajustado: cada recebível deslocado pelo atraso médio do cliente nos últimos 12 meses; clientes com mais de 20% vencido há mais de 30 dias entram com o valor reduzido.',
  revenue: 'Receita bruta do mês e acumulada no ano, a mesma linha do DRE, contra a meta definida em Configurações → Parâmetros.',
  provisional: 'Até a emissão de notas no sistema, o faturamento é a data de emissão dos títulos.',
  overdue: 'Saldo em aberto dos títulos a receber com vencimento antes de hoje, separado pelos dias de atraso, como na tela Inadimplência.',
  commercial: 'Orçamentos que nasceram nos últimos 90 dias. Em aberto: rascunhos e enviados, ainda sem decisão. Conversão: aprovados ÷ decididos. Vencendo: enviados que vencem em até 7 dias.',
  decisions: 'O que espera a sua decisão na pauta: ordens de pagamento, aditivos, qualidade, pedidos de compra e ajustes de ponto. Cada item só aparece para quem pode decidir.',
  updated: 'O painel guarda cada número por até 1 minuto. Quando um recebimento, uma ordem de pagamento ou um contrato muda, os números são recalculados. "Atualizar" lê tudo de novo.',
}

export type Scenario = 'both' | 'base' | 'adjusted'

export const SCENARIOS: { value: Scenario; label: string }[] = [
  { value: 'both', label: 'Os dois' },
  { value: 'base', label: 'Base' },
  { value: 'adjusted', label: 'Ajustado' },
]

/** Cenário na URL do painel: ?cenario=base ou ?cenario=ajustado; sem o parâmetro, "Os dois". */
export const scenarioOf = (value: unknown): Scenario => (value === 'base' ? 'base' : value === 'ajustado' ? 'adjusted' : 'both')

export const scenarioParam = (scenario: Scenario) => (scenario === 'both' ? undefined : scenario === 'base' ? 'base' : 'ajustado')

/** Fluxo de caixa no cenário escolhido (drill-down do card, CA 7): semanal, 90 dias, com o cenário da tela da FIN-07. */
export function cashFlowHref(scenario: Scenario) {
  return `/app/financeiro/fluxo-de-caixa?visao=semanal&horizonte=90&cenario=${scenario === 'base' ? 'base' : scenario === 'adjusted' ? 'ajustado' : 'os-dois'}`
}

/** DRE gerencial (FIN-08), onde está a linha Receita bruta do mês do card. */
export const REVENUE_ORIGIN = '/app/financeiro/dre'

/** Link com busca ("/app/x?a=1#h") nas partes que o navigate do TanStack Router pede. */
export function linkParts(href: string): { to: string; search: Record<string, string>; hash?: string } {
  const url = new URL(href, 'http://local')
  return { to: url.pathname, search: Object.fromEntries(url.searchParams), hash: url.hash ? url.hash.slice(1) : undefined }
}

/** Pontos do gráfico de caixa: semana a semana, só as séries do cenário escolhido. */
export function cashPoints(weeks: readonly CashWeekItem[], scenario: Scenario) {
  return weeks.map((w) => ({
    semana: formatDate(w.weekEnd).slice(0, 5),
    base: scenario === 'adjusted' ? undefined : w.baseBalance,
    ajustado: scenario === 'base' ? undefined : w.adjustedBalance,
  }))
}

const longDate = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' })
const monthName = new Intl.DateTimeFormat('pt-BR', { month: 'long', timeZone: 'UTC' })

/** "Sexta-feira, 16 de outubro" a partir da data de negócio (sem converter fuso). */
export function longDateOf(iso: string) {
  const [y, m, d] = iso.split('-').map(Number)
  const text = longDate.format(new Date(Date.UTC(y, m - 1, d, 12)))
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** "outubro" para o mês 10. */
export const monthOf = (month: number) => monthName.format(new Date(Date.UTC(2026, month - 1, 15)))


/** "Bom dia, John" ou só "Bom dia" sem nome. */
export const greetingOf = (greeting: string, firstName: string) => (firstName ? `${greeting}, ${firstName}` : greeting)

/** "11 dias úteis" / "1 dia útil" / "último dia útil já passou". */
export function workingDaysLabel(days: number) {
  if (days <= 0) return 'sem dias úteis no resto do mês'
  return days === 1 ? 'falta 1 dia útil' : `faltam ${days} dias úteis`
}

/** Largura do medidor de meta (0 a 100). */
export const meterWidth = (pct: number | null | undefined) => (typeof pct === 'number' ? Math.max(0, Math.min(100, pct)) : 0)

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`
