import type { MilestoneTrigger, QuoteMilestoneInput, QuoteMilestoneView, TriggerEvent } from '@/api/generated/model'

export const EVENTS: { value: NonNullable<TriggerEvent>; label: string }[] = [
  { value: 'Signature', label: 'Assinatura do contrato' },
  { value: 'FabricationDone', label: 'Fabricação concluída' },
  { value: 'Delivered', label: 'Entrega' },
  { value: 'TechnicalAcceptance', label: 'Aceite técnico' },
]

export interface MilestoneDraft { description: string; percent: string | null; trigger: MilestoneTrigger; triggerEvent: NonNullable<TriggerEvent>; dueDate: string; days: string }

export const emptyMilestone = (): MilestoneDraft => ({ description: '', percent: null, trigger: 'Event', triggerEvent: 'Signature', dueDate: '', days: '0' })

export const fromView = (m: QuoteMilestoneView | QuoteMilestoneInput): MilestoneDraft => ({
  description: m.description, percent: String(m.percent), trigger: m.trigger, triggerEvent: m.triggerEvent ?? 'Signature', dueDate: m.dueDate ?? '', days: String(m.daysAfterEvent ?? 0),
})

export const toInput = (m: MilestoneDraft): QuoteMilestoneInput => ({
  description: m.description.trim(), percent: m.percent ? Number(m.percent) : 0, trigger: m.trigger, triggerEvent: m.trigger === 'Event' ? m.triggerEvent : null,
  dueDate: m.trigger === 'Date' ? m.dueDate || null : null, daysAfterEvent: m.trigger === 'Event' ? Number(m.days || 0) : null,
})

const round4 = (v: number) => Math.round(v * 10000) / 10000

/** Valores dos marcos como o servidor (PercentSplit): cada um pelo %, o último absorve os centavos quando fecha 100%. */
export function amounts(total: number, percents: number[]) {
  const sum = round4(percents.reduce((a, b) => a + b, 0))
  let allocated = 0
  return percents.map((p, i) => {
    const value = i === percents.length - 1 && sum === 100 ? Math.round((total - allocated) * 100) / 100 : Math.round(total * p) / 100
    allocated += value
    return value
  })
}

/** "Dividir igualmente": 4 casas, o último absorve a diferença. */
export function equalPercents(count: number) {
  if (count <= 0) return []
  const each = Math.floor((100 / count) * 10000) / 10000
  return [...Array.from({ length: count - 1 }, () => each), round4(100 - each * (count - 1))]
}

/** O que falta para 100% ("Faltam 10,00%" ou "Passou 5,00%"), ou null quando fecha. */
export function remaining(percents: number[]) {
  const diff = round4(100 - percents.reduce((a, b) => a + b, 0))
  if (diff === 0) return null
  const fmt = Math.abs(diff).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 4 })
  return diff > 0 ? `Faltam ${fmt}%` : `Passou ${fmt}%`
}
