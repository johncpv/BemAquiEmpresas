import { describe, expect, it } from 'vitest'
import { checkNote, emptyDraft, formatDay, kindLabel, nextMonthStart, statusChip, TAX_KINDS, toContent, toDraft } from './tax-tables'

describe('tabelas da folha (FLH-01)', () => {
  it('os sete tipos, com rótulo em português', () => {
    expect(TAX_KINDS.map((k) => k.kind)).toEqual(['Inss', 'Irrf', 'FamilyAllowance', 'MinimumWage', 'Fgts', 'TransportVoucher', 'EmployerCharges'])
    expect(kindLabel('FamilyAllowance')).toBe('Salário-família')
    expect(kindLabel('Outro')).toBe('Outro')
  })

  it('chips: a confirmar, conferida, substituída e "A partir de" na futura (CA 14)', () => {
    expect(statusChip('Unconfirmed', false, '2041-01-01')).toEqual([{ label: 'A confirmar com a contadora', tone: 'warn' }])
    expect(statusChip('Active', true, '2041-05-01')).toEqual([{ label: 'Conferida', tone: 'ok' }, { label: 'A partir de 01/05/2041', tone: 'info' }])
    expect(statusChip('Superseded', true, '2041-05-01')).toEqual([{ label: 'Substituída', tone: 'neutral' }])
  })

  it('a nova versão abre no primeiro dia do mês seguinte', () => {
    expect(nextMonthStart(new Date(2026, 9, 31))).toBe('2026-11-01')
    expect(nextMonthStart(new Date(2026, 11, 15))).toBe('2027-01-01')
    expect(formatDay('2026-01-01')).toBe('01/01/2026')
  })

  it('IRRF vai e volta pelo editor sem perder faixa, parcela, redução nem a última "acima de"', () => {
    const content = {
      irrf: {
        brackets: [{ upTo: 2000, rate: 0, deduction: 0 }, { upTo: null, rate: 27.5, deduction: 908.73 }],
        dependentDeduction: 100, simplifiedDiscount: 400,
        reductions: [{ upTo: 4000, fixedAmount: 150, rate: 0 }, { upTo: 6000, fixedAmount: 600, rate: 13.3145 }],
      },
    }
    const draft = toDraft('Irrf', content)
    expect(draft.brackets[1]).toEqual({ upTo: null, rate: '27.5', deduction: '908.73' })
    expect(draft.values.dependentDeduction).toBe('100')
    expect(toContent('Irrf', draft)).toEqual(content)
  })

  it('INSS e encargos: só a seção do tipo; campo vazio vai como zero para o servidor validar', () => {
    const inss = emptyDraft('Inss')
    inss.brackets = [{ upTo: '1000', rate: '10', deduction: null }, { upTo: '', rate: null, deduction: null }]
    expect(toContent('Inss', inss)).toEqual({ inss: { brackets: [{ upTo: 1000, rate: 10 }, { upTo: null, rate: 0 }] } })
    const charges = toDraft('EmployerCharges', { employerCharges: { taxRegime: 'Lucro Presumido', employerRate: 20, ratRate: 3, fap: 1, thirdPartiesRate: 5.8 } })
    expect(charges.taxRegime).toBe('Lucro Presumido')
    expect(toContent('EmployerCharges', { ...charges, values: { ...charges.values, fap: '' } })).toEqual({
      employerCharges: { taxRegime: 'Lucro Presumido', employerRate: 20, ratRate: 3, fap: 0, thirdPartiesRate: 5.8 },
    })
    expect(toDraft('MinimumWage', null)).toEqual(emptyDraft('MinimumWage'))
  })

  it('nota da conferência como o servidor grava (CA 5)', () => {
    expect(checkNote(' Contadora X ', '2026-10-01')).toBe('Conferido com Contadora X em 01/10/2026')
  })
})
