import { describe, expect, it } from 'vitest'
import { bankChip, bankDraftErrors, bankMismatches, dependentErrors, emptyBankDraft, emptyDependentDraft, maskCpf, maskName, mismatchConfirmed, pixKeyError } from './employee-complement'

const CPF = '52998224725'
const OTHER = '11144477735'

describe('chave Pix pelo tipo (CAD-04c2 CA 4)', () => {
  it('valida CPF, e-mail, celular e chave aleatória', () => {
    expect(pixKeyError('Cpf', '529.982.247-25')).toBeUndefined()
    expect(pixKeyError('Cpf', '529.982.247-24')).toMatch(/CPF válido/)
    expect(pixKeyError('Email', 'maria@empresa.com.br')).toBeUndefined()
    expect(pixKeyError('Email', 'maria@empresa')).toMatch(/e-mail/)
    expect(pixKeyError('Phone', '(11) 98765-4321')).toBeUndefined()
    expect(pixKeyError('Phone', '98765-4321')).toMatch(/DDD/)
    expect(pixKeyError('Random', '123e4567-e89b-12d3-a456-426614174000')).toBeUndefined()
    expect(pixKeyError('Random', '123e4567e89b')).toMatch(/aleatória/)
  })
})

describe('conta para pagamento (CAD-04c2 CA 4 e 5)', () => {
  it('vem com o titular e a chave Pix do funcionário (propositivo)', () => {
    const d = emptyBankDraft('Maria Silva', CPF)
    expect(d).toMatchObject({ holderName: 'Maria Silva', holderCpf: CPF, pixKeyType: 'Cpf', pixKey: CPF })
    expect(bankDraftErrors(d)).toEqual({})
    expect(bankMismatches(d, CPF)).toEqual([])
  })

  it('banco incompleto e titular sem CPF são recusados', () => {
    const errors = bankDraftErrors({ ...emptyBankDraft('Maria', CPF), bankCode: '77', agency: '', holderCpf: '123' })
    expect(Object.keys(errors).sort()).toEqual(['account', 'agency', 'bankCode', 'holderCpf'])
    expect(bankDraftErrors({ ...emptyBankDraft('Maria', CPF), pixKeyType: '', pixKey: '' }).bankCode).toMatch(/ou uma chave Pix/)
  })

  it('lista o que não bate, mascarado, e exige a marca e o motivo', () => {
    const d = { ...emptyBankDraft('Maria Silva', CPF), holderName: 'João Souza', holderCpf: OTHER, pixKey: OTHER }
    expect(bankMismatches(d, CPF).map((m) => m.text)).toEqual(['Titular: J*** S****, CPF ***.444.777-**', 'Chave Pix: CPF ***.444.777-**'])
    expect(mismatchConfirmed(false, 'Conta conjunta conferida')).toBe(false)
    expect(mismatchConfirmed(true, 'curto')).toBe(false)
    expect(mismatchConfirmed(true, 'Conta conjunta conferida')).toBe(true)
  })

  it('máscaras de CPF e nome', () => {
    expect(maskCpf('12345678909')).toBe('***.456.789-**')
    expect(maskName('Maria da Silva')).toBe('M**** d* S****')
  })

  it('chip de quarentena e de revisada (CA 7 e 8)', () => {
    const date = (iso: string) => iso.slice(0, 10)
    expect(bankChip({ inQuarantine: true, quarantineUntil: '2026-11-25T12:00:00Z', selfReviewed: false }, date)).toEqual({ tone: 'warn', label: 'Conta em quarentena até 2026-11-25' })
    expect(bankChip({ inQuarantine: false, reviewedAt: '2026-10-30T12:00:00Z', selfReviewed: true }, date)?.label).toBe('Conta revisada em 2026-10-30 (autorrevisado)')
    expect(bankChip({ inQuarantine: false, selfReviewed: false }, date)).toBeNull()
  })
})

describe('dependente (CAD-04c2 CA 1 e 2)', () => {
  it('CPF obrigatório e válido só no dependente de IR', () => {
    const base = { ...emptyDependentDraft(), name: 'Ana', birthDate: '2015-02-03', kinship: 'Child' as const }
    expect(dependentErrors(base, '2026-10-26')).toEqual({})
    expect(dependentErrors({ ...base, irDependent: true }, '2026-10-26').cpf).toMatch(/Informe o CPF/)
    expect(dependentErrors({ ...base, irDependent: true }, '2026-10-26', true)).toEqual({})
    expect(dependentErrors({ ...base, irDependent: true, cpf: '11111111111' }, '2026-10-26').cpf).toBe('CPF inválido.')
    expect(dependentErrors({ ...base, birthDate: '2030-01-01' }, '2026-10-26').birthDate).toBeDefined()
  })
})
