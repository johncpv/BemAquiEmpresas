import type { ApprovalPreview } from '@/api/generated/model'

export interface OnboardingDraft {
  enabled: boolean
  milestones: Record<string, { dueDate: string | null; receivedBefore: boolean }>
}

const addDays = (iso: string, days: number) => { const d = new Date(iso + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10) }

/** Data que decide o "recebido antes": a do marco por data ou a assinatura + prazo; marco por outro evento não tem data. */
export function milestoneDate(m: ApprovalPreview['milestones'][number], signedAt: string, dueDate: string | null) {
  if (m.trigger === 'Date') return dueDate
  return m.triggerEvent === 'Signature' && signedAt ? addDays(signedAt, m.daysAfterEvent ?? 0) : null
}

/** Pré-marca como recebido antes os marcos com data até a data de corte (COM-04c, propositivo). */
export function receivedBy(preview: ApprovalPreview, signedAt: string, cutover: string | null, current: OnboardingDraft['milestones']): OnboardingDraft {
  const milestones: OnboardingDraft['milestones'] = {}
  for (const m of preview.milestones) {
    const dueDate = current[m.id]?.dueDate ?? m.dueDate ?? null
    const date = milestoneDate(m, signedAt, dueDate)
    milestones[m.id] = { dueDate, receivedBefore: !!cutover && !!date && date <= cutover }
  }
  return { enabled: true, milestones }
}

/** Corpo do modo implantação na aprovação. */
export const onboardingBody = (draft: OnboardingDraft) => draft.enabled
  ? { isOnboarding: true, milestones: Object.entries(draft.milestones).map(([milestoneId, m]) => ({ milestoneId, dueDate: m.dueDate, receivedBefore: m.receivedBefore })) }
  : {}

/** Implantação concluída há mais de 90 dias (COM-04c CA 7): aviso âmbar no diálogo. */
export const openingIsOld = (completedAt: string | null, now = Date.now()) => (completedAt ? now - Date.parse(completedAt) > 90 * 86_400_000 : false)
