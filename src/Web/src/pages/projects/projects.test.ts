import { describe, expect, it } from 'vitest'
import { delayDays, PROJECT_DICAS } from './projects'

describe('projeto 360 (COM-05)', () => {
  it('atraso só enquanto não entregue', () => {
    expect(delayDays('2026-10-10', 'Planned', '2026-10-20')).toBe(10)
    expect(delayDays('2026-10-10', 'Delivered', '2026-10-20')).toBeNull()
    expect(delayDays('2026-10-25', 'Planned', '2026-10-20')).toBeNull()
    expect(delayDays(null, 'Planned', '2026-10-20')).toBeNull()
  })

  it('dicas das três colunas (PRD-04 CA 9)', () => {
    expect(PROJECT_DICAS.comprometido).toBe('Já contratado, mas ainda não gasto: título previsto ou material comprado que não foi baixado para a OS.')
    expect(PROJECT_DICAS.realizado).not.toContain('Fase 3')
  })
})
