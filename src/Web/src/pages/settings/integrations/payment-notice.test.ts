import { describe, expect, it } from 'vitest'
import { modeLabel, reportText } from './payment-notice'

describe('aviso de pagamento do Inter (BNK-02)', () => {
  it('mostra o modo do endereço sem o token', () => {
    expect(modeLabel('Query')).toContain('?t=')
    expect(modeLabel('PathToken')).toContain('caminho')
  })

  it('resume a conferência diária', () => {
    expect(reportText({ consulted: 12, settled: 3, divergent: 1, unknown: 0, failed: 0 })).toBe('12 consultados · 3 baixados · 1 a conferir')
    expect(reportText({ consulted: 2, settled: 0, divergent: 0, unknown: 1, failed: 1 })).toBe('2 consultados · 0 baixados · 0 a conferir · 1 fora do sistema · 1 com falha')
  })
})
