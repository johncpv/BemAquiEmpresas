import type { OrderForecastInfo, PlanningWarning, ScheduleRunInfo, ScheduleTrigger, StepForecastInfo } from '@/api/generated/model'
import { formatDate } from '@/lib/format'

/** Dicas do Planejamento (PLN-01a): "Previsto", "Horas restantes" e "Sem previsão". */
export const PLANNING_DICAS = {
  previsto: 'Data calculada pelo plano a partir da fila, das horas que faltam e das horas que cada centro tem por dia. Muda sozinha quando o plano é recalculado; ninguém digita.',
  horasRestantes: 'Horas padrão menos as apontadas. Etapa em andamento fica com pelo menos 10% do padrão; etapa refeita pela qualidade volta com as horas padrão inteiras.',
  semPrevisao: 'O plano não conseguiu datar esta etapa. O motivo diz o que falta: o centro de trabalho da etapa, o roteiro da OS ou horas no calendário do centro.',
} as const

export const TRIGGER_LABEL: Record<ScheduleTrigger, string> = { Hourly: 'de hora em hora', Manual: 'manual', Event: 'por evento' }

const time = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit' })

/** "14:05" no horário de Brasília. */
export function hhmm(instant: string): string {
  return time.format(new Date(instant))
}

/** CA 14: "Recalculado às {hh:mm} ({gatilho}), {n} OS em {ms} ms". */
export function runLine(run: ScheduleRunInfo): string {
  const at = run.finishedAt ?? run.startedAt ?? run.requestedAt
  const orders = run.ordersCount === 1 ? '1 OS' : `${run.ordersCount} OS`
  return `Recalculado às ${hhmm(at)} (${TRIGGER_LABEL[run.trigger]}), ${orders} em ${run.durationMs} ms`
}

/** "O último cálculo falhou às {hh:mm}. As datas mostradas são do cálculo anterior." */
export function failedLine(run: ScheduleRunInfo): string {
  return `O último cálculo falhou às ${hhmm(run.finishedAt ?? run.requestedAt)}. As datas mostradas são do cálculo anterior.`
}

/** "Previsto 30/10/2026 → 03/11/2026" (o mesmo dia aparece uma vez). */
export function forecastText(f: { plannedStart?: string | null; plannedEnd?: string | null; noForecastReason?: string | null }): string {
  if (f.noForecastReason) return `Sem previsão: ${f.noForecastReason}`
  if (!f.plannedStart || !f.plannedEnd) return 'Sem previsão'
  return f.plannedStart === f.plannedEnd ? `Previsto ${formatDate(f.plannedStart)}` : `Previsto ${formatDate(f.plannedStart)} → ${formatDate(f.plannedEnd)}`
}

/** "Fim previsto 05/11/2026" ou "Sem previsão: {motivo}" (CA 16). */
export function orderEndText(f: OrderForecastInfo): string {
  if (f.noForecastReason) return `Sem previsão: ${f.noForecastReason}`
  return f.plannedEnd ? `Fim previsto ${formatDate(f.plannedEnd)}` : 'Sem previsão'
}

/** Horas com uma casa, só na exibição (regra da story). */
export function hoursText(value: number): string {
  return `${value.toLocaleString('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 1 })} h`
}

export function stepForecast(forecast: OrderForecastInfo | undefined, stepId: string): StepForecastInfo | undefined {
  return forecast?.steps.find((s) => s.stepId === stepId)
}

export interface WarningAction {
  label: string
  to: string
  params?: Record<string, string>
  search?: Record<string, string>
}

/** Propositivo: cada "Sem previsão" leva à ação que resolve. */
export function warningAction(w: PlanningWarning): WarningAction {
  switch (w.action) {
    case 'ChooseRouting':
      return { label: 'Escolher roteiro', to: '/app/producao/os/$id', params: { id: w.orderId } }
    case 'SetWorkCenter':
      return { label: 'Informar centro', to: '/app/producao/os/$id', params: { id: w.orderId }, search: { aba: 'etapas' } }
    default:
      return { label: 'Revisar calendário', to: '/app/producao/calendario' }
  }
}

/** Ids em lotes de 100 (a URL da consulta fica curta mesmo com o Kanban cheio). */
export function chunks<T>(items: readonly T[], size = 100): T[][] {
  const result: T[][] = []
  for (let i = 0; i < items.length; i += size) result.push(items.slice(i, i + size))
  return result
}
