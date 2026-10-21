import type { AmendmentMilestoneInput, TriggerEvent } from '@/api/generated/model'
import type { Tone } from '../../finance/receivables/receivables'

export const AMENDMENT_STATUS: Record<string, { label: string; tone: Tone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  PendingApproval: { label: 'Aguardando aprovação', tone: 'warn' },
  Approved: { label: 'Aprovado', tone: 'ok' },
  Rejected: { label: 'Reprovado', tone: 'neutral' },
}

export const AMENDMENT_DICAS = {
  vigente: 'Valor original do contrato mais os aditivos aprovados. O contrato original não muda.',
  reducao: 'Na redução, o sistema tira dos títulos futuros sem baixa, do último para o primeiro. Dá para ajustar o valor novo de cada um.',
}

export interface Reducible { id: string; number: string; amount: number }

/** Redução proposta (COM-07 CA 3): do último título para o primeiro, até cobrir a redução; null se não cabe. */
export function proposeReductions(reducible: Reducible[], reduction: number): { receivableId: string; newAmount: number }[] | null {
  let left = Math.round(reduction * 100)
  const result: { receivableId: string; newAmount: number }[] = []
  for (const r of reducible) {
    if (left <= 0) break
    const cents = Math.round(r.amount * 100)
    const take = Math.min(cents, left)
    result.push({ receivableId: r.id, newAmount: (cents - take) / 100 })
    left -= take
  }
  return left > 0 ? null : result
}

/** Valores dos marcos do acréscimo pelo maior resto, como o servidor (só para a prévia). */
export function milestoneAmounts(value: number, percents: number[]) {
  const total = Math.round(value * 100)
  const raw = percents.map((p) => (total * p) / 100)
  const floors = raw.map(Math.floor)
  let left = total - floors.reduce((a, b) => a + b, 0)
  const order = raw.map((r, i) => ({ rest: r - Math.floor(r), i })).sort((a, b) => b.rest - a.rest || a.i - b.i)
  for (const o of order) { if (left <= 0) break; floors[o.i] += 1; left -= 1 }
  return floors.map((c) => c / 100)
}

export interface MilestoneDraft { description: string; percent: string | null; trigger: 'Date' | 'Event'; triggerEvent: string; dueDate: string; days: string }

export const emptyMilestone = (): MilestoneDraft => ({ description: '', percent: null, trigger: 'Event', triggerEvent: 'Signature', dueDate: '', days: '0' })

export const milestoneInputs = (rows: MilestoneDraft[]): AmendmentMilestoneInput[] => rows.map((r) => ({
  description: r.description.trim(), percent: Number(r.percent ?? 0), trigger: r.trigger, triggerEvent: r.trigger === 'Event' ? (r.triggerEvent as TriggerEvent) : null,
  dueDate: r.trigger === 'Date' ? r.dueDate || null : null, daysAfterEvent: r.trigger === 'Event' ? Number(r.days || 0) : null,
}))
