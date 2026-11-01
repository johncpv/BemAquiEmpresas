import { describe, expect, it } from 'vitest'
import { CALCULATION_LABELS, choosesEmployees, currentPeriod, formatPct, isManualCalculation, MODE_LABELS, periodLabel, totals, typeLabel } from './payroll'

describe('folha lançada (RH-02a)', () => {
  it('totais com a mesma regra do servidor: a informativa fica fora do líquido (CA 6)', () => {
    expect(totals([{ kind: 'Earning', amount: 3000 }, { kind: 'Earning', amount: 450.55 }, { kind: 'Deduction', amount: 330 }, { kind: 'Deduction', amount: 120.45 }, { kind: 'Information', amount: 276 }]))
      .toEqual({ gross: 3450.55, deductions: 450.45, net: 3000.1 })
    expect(totals([{ kind: 'Earning', amount: 100 }, { kind: 'Deduction', amount: 150 }]).net).toBe(-50)
    expect(totals([{ kind: null, amount: 10 }, { kind: 'Earning', amount: null }])).toEqual({ gross: 0, deductions: 0, net: 0 })
  })

  it('competência, tipos e variação', () => {
    expect(periodLabel('2026-10-01')).toBe('outubro de 2026')
    expect(periodLabel('2027-01')).toBe('janeiro de 2027')
    expect(currentPeriod(new Date(2026, 9, 27))).toBe('2026-10')
    expect(typeLabel('Thirteenth2')).toBe('13º 2ª parcela')
    expect(choosesEmployees('Vacation')).toBe(true)
    expect(choosesEmployees('Monthly')).toBe(false)
    expect(formatPct(15)).toBe('+15%')
    expect(formatPct(-10.5)).toBe('-10,5%')
  })
})

describe('folha calculada (FLH-02a)', () => {
  it('avulso só em rubrica sem regra ou no vale-transporte fornecido', () => {
    expect(isManualCalculation('None')).toBe(true)
    expect(isManualCalculation(undefined)).toBe(true)
    expect(isManualCalculation('TransportVoucherSupplied')).toBe(true)
    expect(isManualCalculation('Inss')).toBe(false)
    expect(isManualCalculation('BaseSalary')).toBe(false)
  })

  it('toda regra tem rótulo e o modo aparece por extenso', () => {
    for (const rule of ['None', 'BaseSalary', 'SickLeave', 'Insalubrity', 'HazardPay', 'Absence', 'Lateness', 'FamilyAllowance', 'TransportVoucher', 'TransportVoucherSupplied', 'Inss', 'Irrf', 'Fgts', 'VacationPay', 'VacationAverages', 'VacationThird', 'VacationSale', 'VacationSaleThird']) {
      expect(CALCULATION_LABELS[rule]).toBeTruthy()
    }

    expect(MODE_LABELS.Calculated).toBe('Folha calculada')
  })
})
