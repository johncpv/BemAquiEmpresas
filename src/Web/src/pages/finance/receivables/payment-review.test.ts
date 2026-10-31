import { describe, expect, it } from 'vitest'
import type { PaymentReviewOption } from '@/api/generated/model'
import { differenceKind, pickOption } from './payment-review'

const option = (decision: string, allowed = true): PaymentReviewOption => ({
  decision, label: decision, principal: 0, interest: 0, discount: 0, balanceBefore: 1000, balanceAfter: 0, requiresApproval: !allowed, allowed,
})

describe('conferir pagamento (BNK-02 CA 9)', () => {
  it('diz se pagou a menos ou a mais', () => {
    expect(differenceKind(-100)).toBe('less')
    expect(differenceKind(15)).toBe('more')
    expect(differenceKind(0)).toBe('equal')
  })

  it('vem com a opção sugerida, mas respeita a escolha', () => {
    const options = [option('Discount', false), option('KeepOpen')]
    expect(pickOption(options, null, 'KeepOpen')?.decision).toBe('KeepOpen')
    expect(pickOption(options, 'Discount', 'KeepOpen')?.decision).toBe('Discount')
    expect(pickOption(options, null, 'Inexistente')?.decision).toBe('KeepOpen')
  })
})
