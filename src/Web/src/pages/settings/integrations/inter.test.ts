import { afterEach, describe, expect, it, vi } from 'vitest'
import { requestStepUp, setStepUpHandler } from '@/api/step-up'
import { expiryText, scopeLabel, situationOf } from './inter'

describe('Banco Inter (BNK-01a)', () => {
  it('situação com o tom semântico (nunca o acento)', () => {
    expect(situationOf('NotConnected')).toEqual({ label: 'Não conectada', tone: 'neutral' })
    expect(situationOf('Sandbox').tone).toBe('info')
    expect(situationOf('Production').tone).toBe('ok')
    expect(situationOf('ExpiringSoon').tone).toBe('warn')
    expect(situationOf('Expired').tone).toBe('crit')
  })

  it('escopos e vencimento em PT-BR', () => {
    expect(scopeLabel('extrato.read')).toBe('Extrato (consultar saldo e extrato)')
    expect(scopeLabel('outro.escopo')).toBe('outro.escopo')
    expect(expiryText(10)).toBe('vence em 10 dias')
    expect(expiryText(1)).toBe('vence amanhã')
    expect(expiryText(-1)).toBe('vencido')
  })
})

describe('2FA de novo (CA 1)', () => {
  afterEach(() => setStepUpHandler(null))

  it('sem diálogo montado, a recusa segue para quem chamou', async () => {
    await expect(requestStepUp(true)).resolves.toBe(false)
  })

  it('várias recusas juntas esperam a mesma confirmação', async () => {
    let resolve!: (v: boolean) => void
    const handler = vi.fn(() => new Promise<boolean>((r) => { resolve = r }))
    setStepUpHandler(handler)
    const a = requestStepUp(true)
    const b = requestStepUp(true)
    resolve(true)
    await expect(Promise.all([a, b])).resolves.toEqual([true, true])
    expect(handler).toHaveBeenCalledTimes(1)
  })
})
