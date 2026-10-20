import { describe, expect, it } from 'vitest'
import type { ApprovalPreview } from '@/api/generated/model'
import { onboardingBody, receivedBy } from './onboarding'

const preview = {
  milestones: [
    { id: 's', description: 'Sinal', percent: 20, amount: 200, when: 'na assinatura', trigger: 'Event', triggerEvent: 'Signature', dueDate: null, daysAfterEvent: 0 },
    { id: 'd', description: 'Parcela', percent: 30, amount: 300, when: 'em 10/10/2026', trigger: 'Date', triggerEvent: null, dueDate: '2026-10-10', daysAfterEvent: null },
    { id: 'f', description: 'Fabricação', percent: 50, amount: 500, when: '10 dias após fabricação', trigger: 'Event', triggerEvent: 'FabricationDone', dueDate: null, daysAfterEvent: 10 },
  ],
} as unknown as ApprovalPreview

describe('contrato de implantação (COM-04c)', () => {
  it('pré-marca o que tem data até o corte', () => {
    const draft = receivedBy(preview, '2026-08-01', '2026-09-30', {})
    expect(draft.milestones.s.receivedBefore).toBe(true)
    expect(draft.milestones.d.receivedBefore).toBe(false)
    expect(draft.milestones.f.receivedBefore).toBe(false)
  })

  it('monta o corpo só quando ligado', () => {
    expect(onboardingBody({ enabled: false, milestones: {} })).toEqual({})
    expect(onboardingBody(receivedBy(preview, '2026-08-01', '2026-09-30', {}))).toMatchObject({ isOnboarding: true })
  })
})
