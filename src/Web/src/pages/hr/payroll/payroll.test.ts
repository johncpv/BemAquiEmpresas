import { describe, expect, it } from 'vitest'
import { CALCULATION_LABELS, choosesEmployees, currentPeriod, formatPct, hoursLabel, isManualCalculation, MODE_LABELS, parseHours, periodLabel, totals, typeLabel } from './payroll'

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
    for (const rule of ['None', 'BaseSalary', 'SickLeave', 'Insalubrity', 'HazardPay', 'Absence', 'Lateness', 'FamilyAllowance', 'TransportVoucher', 'TransportVoucherSupplied', 'Inss', 'Irrf', 'Fgts',
      'OvertimeWorkday', 'OvertimeRestDay', 'OvertimeHoliday', 'NightShift', 'Dsr', 'DsrLoss', 'HourBankPayout', 'HourBankDiscount',
      'VacationPay', 'VacationAverages', 'VacationThird', 'VacationSale', 'VacationSaleThird']) {
      expect(CALCULATION_LABELS[rule]).toBeTruthy()
    }

    expect(MODE_LABELS.Calculated).toBe('Folha calculada')
  })
})

describe('banco de horas na folha (FLH-02b)', () => {
  it('mostra as horas com o sinal e lê o que o RH digita', () => {
    expect(hoursLabel(3000)).toBe('50 h')
    expect(hoursLabel(630, true)).toBe('+10 h 30 min')
    expect(hoursLabel(-300)).toBe('−5 h')
    expect(hoursLabel(45)).toBe('45 min')
    expect(parseHours('10')).toBe(600)
    expect(parseHours('10,5')).toBe(630)
    expect(parseHours('2:15')).toBe(135)
    expect(parseHours('0')).toBeNull()
    expect(parseHours('abc')).toBeNull()
  })
})
