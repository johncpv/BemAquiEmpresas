import type { CashFlowAdjusted, CashFlowPeriod, CashFlowResult } from '@/api/generated/model'
import type { ChartSeries } from '@/components/ui/chart'
import { formatDate, formatMoney } from '@/lib/format'

export const VIEWS = [
  { value: 'diaria', api: 'daily', label: 'Diária' },
  { value: 'semanal', api: 'weekly', label: 'Semanal' },
  { value: 'mensal', api: 'monthly', label: 'Mensal' },
] as const

export const HORIZONS = [30, 60, 90] as const

/** Cenários do fluxo (FIN-07 CA 7): na URL, padrão "Os dois". */
export const SCENARIOS = [
  { value: 'os-dois', api: 'both', label: 'Os dois' },
  { value: 'base', api: 'base', label: 'Base' },
  { value: 'ajustado', api: 'adjusted', label: 'Ajustado' },
] as const

export type Scenario = (typeof SCENARIOS)[number]['api']

export const CASH_FLOW_DICAS = {
  realizado: 'O que já entrou e saiu das contas: baixas, pagamentos e lançamentos.',
  projetado: 'O que deve entrar e sair: títulos em aberto pelo saldo, no vencimento, e as despesas recorrentes do mês.',
  vencidos: 'Títulos com vencimento passado e ainda em aberto. Entram no dia de hoje.',
  estimado: 'Data estimada, como o pagamento do instalador antes da OS terminar.',
  semData: 'Parcelas que dependem de um marco (ex.: entrega). Entram no fluxo quando o marco acontecer.',
  base: 'Base: cada título entra no vencimento combinado, pelo saldo.',
  ajustado: 'Ajustado: cada recebimento entra com o atraso de sempre do cliente (média dos últimos 12 meses, em dias corridos). Pagamentos e recorrentes ficam como na base.',
  atrasoMedio: 'Atraso médio: quantos dias, em média, o cliente pagou depois do vencimento nos últimos 12 meses, pesando pelo valor. Pagar antes conta como zero.',
  mediaEmpresa: 'Média da empresa: o atraso médio de todos os clientes. Vale para quem ainda não tem pagamento registrado.',
  inadimplencia: 'Inadimplência: o que o cliente deve vencido há mais de 30 dias, dividido pelo que ele faturou nos últimos 12 meses (títulos de implantação contam na dívida, não no faturado). Até 100%.',
  risco: 'Risco de não receber: para o cliente com inadimplência acima do limite (Configurações → Parâmetros), o ajustado conta só a parte que provavelmente entra: valor × (1 − inadimplência).',
  diferenca: 'Diferença causada por atrasos: menor saldo da base menos o menor saldo do ajustado.',
  menorSaldo: 'Menor saldo previsto: o dia, de hoje até o fim do horizonte, em que o saldo fica mais baixo.',
}

export const KIND_LABELS: Record<string, string> = { Transaction: 'Realizado', Receivable: 'A receber', Payable: 'A pagar', Recurring: 'Recorrente', Payroll: 'Folha' }

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']

/** Rótulo do período: o dia, a semana ("19/10 a 25/10") ou o mês ("out/2026"). */
export function periodLabel(p: Pick<CashFlowPeriod, 'start' | 'end'>, view: string) {
  if (view === 'monthly') return `${MONTHS[Number(p.start.slice(5, 7)) - 1]}/${p.start.slice(0, 4)}`
  if (view === 'weekly') return `${formatDate(p.start).slice(0, 5)} a ${formatDate(p.end).slice(0, 5)}`
  return formatDate(p.start)
}

/** O ajustado só aparece quando a API o trouxe (consolidado) e o cenário não é só a base. */
export function adjustedOf(flow: Pick<CashFlowResult, 'adjusted'>, scenario: Scenario): CashFlowAdjusted | null {
  return scenario === 'base' ? null : flow.adjusted ?? null
}

/**
 * Pontos do gráfico: realizado até hoje e, de hoje em diante, a base e o ajustado (hoje também no realizado, para a linha
 * não quebrar). Com só a base, a série projetada é a da FIN-05.
 */
export function chartPoints(periods: CashFlowPeriod[], view: string, adjusted?: CashFlowAdjusted | null) {
  return periods.map((p, i) => {
    const a = adjusted?.periods[i]
    return {
      periodo: periodLabel(p, view),
      realizado: !p.projected || p.containsToday ? p.balance : undefined,
      projetado: p.projected ? p.balance : undefined,
      ajustado: a && a.projected ? a.balance : undefined,
    }
  })
}

/** Séries do cenário (FIN-07 CA 12): base tracejado fino, ajustado tracejado grosso com marcador, realizado contínuo. */
export function chartSeries(scenario: Scenario, hasAdjusted: boolean): ChartSeries[] {
  const realizado: ChartSeries = { key: 'realizado', label: 'Realizado', kind: 'actual', color: 1 }
  if (!hasAdjusted) return [realizado, { key: 'projetado', label: 'Projetado', kind: 'projected', color: 2 }]
  const ajustado: ChartSeries = { key: 'ajustado', label: 'Ajustado', kind: 'adjusted', color: 3 }
  if (scenario === 'adjusted') return [realizado, ajustado]
  return [realizado, { key: 'projetado', label: 'Base', kind: 'base', color: 2 }, ajustado]
}

/** Linha de hoje e do menor saldo no gráfico, no rótulo do período que contém cada data. */
export function chartReferences(flow: Pick<CashFlowResult, 'periods' | 'view' | 'lowest' | 'today'>, adjusted?: CashFlowAdjusted | null) {
  const at = (date: string) => flow.periods.find((p) => p.start <= date && date <= p.end)
  const refs: { x: string; label: string }[] = []
  const today = at(flow.today)
  if (today) refs.push({ x: periodLabel(today, flow.view), label: 'Hoje' })
  const lowestPoint = adjusted?.lowest ?? flow.lowest
  const lowest = lowestPoint ? at(lowestPoint.date) : undefined
  if (lowest && lowest !== today) refs.push({ x: periodLabel(lowest, flow.view), label: adjusted?.lowest ? 'Menor saldo ajustado' : 'Menor saldo' })
  return refs
}

/** Resumo textual acima do gráfico (FIN-07 CA 11): os dois menores saldos. */
export function chartSummary(flow: Pick<CashFlowResult, 'lowest'>, adjusted?: CashFlowAdjusted | null) {
  if (!flow.lowest) return undefined
  const base = `${formatDate(flow.lowest.date)}: ${formatMoney(flow.lowest.balance)}`
  if (!adjusted?.lowest) return `Menor saldo previsto em ${base}.`
  return `Menor saldo ajustado em ${formatDate(adjusted.lowest.date)}: ${formatMoney(adjusted.lowest.balance)}; na base, em ${base}.`
}
