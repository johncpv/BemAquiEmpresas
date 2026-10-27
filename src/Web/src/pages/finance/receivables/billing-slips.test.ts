import { describe, expect, it } from 'vitest'
import { issuePhase, messageError, needsApproval, slipStatus, SLOW_TEXT, PHASE_TEXT } from './billing-slips'

const draft = { amount: 1000, discountLimitPct: 5, discountLimitAmount: 500, finePct: 2, interestMonthPct: 1 }

describe('boleto (BNK-01b)', () => {
  it('limites que exigem aprovar (CA 5)', () => {
    expect(needsApproval(draft, 50, 2, 1)).toBe(false)
    expect(needsApproval(draft, 50.01, 2, 1)).toBe(true)
    expect(needsApproval({ ...draft, amount: 20_000 }, 600, 2, 1)).toBe(true)
    expect(needsApproval(draft, 0, 2.5, 1)).toBe(true)
    expect(needsApproval(draft, 0, 1, 1.5)).toBe(true)
  })

  it('padrão 6 pelo tempo desde o pedido', () => {
    const at = '2026-10-27T10:00:00Z'
    const t0 = Date.parse(at)
    expect(issuePhase(at, t0 + 1000)).toBe('sent')
    expect(issuePhase(at, t0 + 5000)).toBe('waiting')
    expect(issuePhase(at, t0 + 31_000)).toBe('slow')
    expect(PHASE_TEXT.slow).toBe(SLOW_TEXT)
  })

  it('situação e mensagem ao pagador', () => {
    expect(slipStatus('Requested').label).toBe('Boleto em emissão')
    expect(slipStatus('Issued', true).label).toBe('Cancelamento enviado')
    expect(messageError('a\nb\nc')).toBeUndefined()
    expect(messageError('a\nb\nc\nd')).toBe('Use até 3 linhas.')
    expect(messageError('x'.repeat(79))).toBe('Cada linha tem até 78 caracteres.')
  })
})
