import type { CashFlowPeriod, CashFlowResult } from '@/api/generated/model'
import { formatDate } from '@/lib/format'

export const VIEWS = [
  { value: 'diaria', api: 'daily', label: 'Diária' },
  { value: 'semanal', api: 'weekly', label: 'Semanal' },
  { value: 'mensal', api: 'monthly', label: 'Mensal' },
] as const

export const HORIZONS = [30, 60, 90] as const

export const CASH_FLOW_DICAS = {
  realizado: 'O que já entrou e saiu das contas: baixas, pagamentos e lançamentos.',
  projetado: 'O que deve entrar e sair: títulos em aberto pelo saldo, no vencimento, e as despesas recorrentes do mês.',
  vencidos: 'Títulos com vencimento passado e ainda em aberto. Entram no dia de hoje.',
  estimado: 'Data estimada, como o pagamento do instalador antes da OS terminar.',
  semData: 'Parcelas que dependem de um marco (ex.: entrega). Entram no fluxo quando o marco acontecer.',
}

export const KIND_LABELS: Record<string, string> = { Transaction: 'Realizado', Receivable: 'A receber', Payable: 'A pagar', Recurring: 'Recorrente' }

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']

/** Rótulo do período: o dia, a semana ("19/10 a 25/10") ou o mês ("out/2026"). */
export function periodLabel(p: Pick<CashFlowPeriod, 'start' | 'end'>, view: string) {
  if (view === 'monthly') return `${MONTHS[Number(p.start.slice(5, 7)) - 1]}/${p.start.slice(0, 4)}`
  if (view === 'weekly') return `${formatDate(p.start).slice(0, 5)} a ${formatDate(p.end).slice(0, 5)}`
  return formatDate(p.start)
}

/** Pontos do gráfico: realizado até hoje e projetado de hoje em diante (hoje nas duas, para a linha não quebrar). */
export function chartPoints(periods: CashFlowPeriod[], view: string) {
  return periods.map((p) => ({
    periodo: periodLabel(p, view),
    realizado: !p.projected || p.containsToday ? p.balance : undefined,
    projetado: p.projected ? p.balance : undefined,
  }))
}

/** Linha de hoje e do menor saldo no gráfico, no rótulo do período que contém cada data. */
export function chartReferences(flow: Pick<CashFlowResult, 'periods' | 'view' | 'lowest' | 'today'>) {
  const at = (date: string) => flow.periods.find((p) => p.start <= date && date <= p.end)
  const refs: { x: string; label: string }[] = []
  const today = at(flow.today)
  if (today) refs.push({ x: periodLabel(today, flow.view), label: 'Hoje' })
  const lowest = flow.lowest ? at(flow.lowest.date) : undefined
  if (lowest && lowest !== today) refs.push({ x: periodLabel(lowest, flow.view), label: 'Menor saldo' })
  return refs
}
