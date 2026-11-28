import { describe, expect, it } from 'vitest'
import { modeOf, needsCode, SELF_APPROVAL_OPTIONS } from './self-approval'

describe('autoaprovação da OP (FIN-10a)', () => {
  it('tem as três opções na ordem da tela', () => {
    expect(SELF_APPROVAL_OPTIONS.map((o) => o.label)).toEqual(['Nunca', 'Só o Proprietário', 'Qualquer aprovador (só no 1º nível)'])
  })

  it('lê o modo da API e cai em Nunca quando falta', () => {
    expect(modeOf('OwnerOnly')).toBe('OwnerOnly')
    expect(modeOf('AnyApprover')).toBe('AnyApprover')
    expect(modeOf(undefined)).toBe('Never')
    expect(modeOf('Outro')).toBe('Never')
  })

  it('só pede o código quando o modo muda', () => {
    expect(needsCode('Never', 'Never')).toBe(false)
    expect(needsCode('Never', 'OwnerOnly')).toBe(true)
  })
})
