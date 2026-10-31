import { describe, expect, it } from 'vitest'
import { dueChipOf, exportOf, kindLabel, nextStepOf, requiresIdentity, statusOf } from './privacy'

describe('pedido do titular (LGPD-01a)', () => {
  it('rótulos, situação e chip do prazo', () => {
    expect(kindLabel('Portability')).toBe('Portabilidade')
    expect(statusOf('Refused')).toEqual({ label: 'Recusado', tone: 'neutral' })
    expect(dueChipOf('critical')?.tone).toBe('crit')
    expect(dueChipOf('warning')?.label).toBe('Vence em breve')
    expect(dueChipOf(null)).toBeNull()
    expect(exportOf('Failed')?.tone).toBe('crit')
  })

  it('o tipo sugere o próximo passo e a identidade vem antes', () => {
    expect(requiresIdentity('Confirmation')).toBe(false)
    expect(nextStepOf('Access', false)).toContain('Confira a identidade')
    expect(nextStepOf('Access', true)).toBe('Exporte os dados do titular.')
    expect(nextStepOf('Access', true, 'Ready')).toContain('Baixe a exportação')
    expect(nextStepOf('Correction', true)).toContain('Abra o cadastro')
    expect(nextStepOf('Anonymization', true)).toContain('próxima versão')
  })
})
