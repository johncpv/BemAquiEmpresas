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
  // DSH-01b: depois dos cards da DSH-01a, empilhados.
  margin: 'order-7',
  'active-projects': 'order-8',
  production: 'order-9',
  backlog: 'order-10',
  deliveries: 'order-11',
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
  margin: 'Margem projetada (com custos reais): receita contratada menos o custo projetado dos projetos em andamento (realizado mais o que falta do orçado ou o comprometido), sobre a receita. Margem realizada: a dos projetos entregues ou encerrados no ano, só com o custo já gasto. As duas são ponderadas pela receita e vêm do relatório Orçado × realizado.',
  activeProjects: 'Projetos planejados, em produção, fabricados ou em instalação. Os atrasados (prometido já passou) e os em risco (previsão depois do prometido) vêm primeiro.',
  backlog: 'A produzir: receita contratada (contrato mais aditivos) dos projetos planejados ou em produção. A faturar: contratado dos contratos ativos menos a receita já reconhecida desses projetos, a mesma regra do DRE.',
  production: 'OS atrasadas e em risco pela mesma regra do Kanban. Ocupação: horas comprometidas pelo plano sobre a capacidade da fábrica em cada semana, como em Planejamento → Ocupação.',
  deliveries: 'Previstas no mês: projetos com a data prevista neste mês e ainda não entregues. Em risco: a previsão passou da data prometida ao cliente. Atrasados: a data prometida já passou.',
  toProduce: 'Receita contratada dos projetos que ainda não foram fabricados (planejados ou em produção).',
  toInvoice: 'Contratado dos contratos ativos menos o que já virou receita desses projetos (títulos emitidos, até as notas reais; depois, as notas autorizadas).',
  realizedMargin: 'Margem dos projetos entregues ou encerrados no ano, com o custo já gasto (sem projeção).',
  atRisk: 'A data prevista de entrega passou da data prometida ao cliente.',
  updated: 'O painel guarda cada número por até 1 minuto. Quando um recebimento, uma ordem de pagamento, um contrato, a previsão de um projeto ou um custo do projeto muda, os números são recalculados. "Atualizar" lê tudo de novo.',
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
