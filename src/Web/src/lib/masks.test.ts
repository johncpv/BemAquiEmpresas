import { describe, expect, it } from 'vitest'
import { compareDecimal, formatBrDecimal, parseBrDecimal } from './decimal'
import { cepMask, documentKind, documentMask, isValidCnpj, isValidCpf, ncmMask, phoneMask } from './masks'

describe('máscaras (FND-09d CA 3 e 4)', () => {
  it('CPF ou CNPJ no mesmo campo, inclusive o CNPJ alfanumérico', () => {
    expect(documentMask.format(documentMask.normalize('12345678909'))).toBe('123.456.789-09')
    expect(documentMask.format(documentMask.normalize('47225005000186'))).toBe('47.225.005/0001-86')
    expect(documentMask.format(documentMask.normalize('12abc34501de35'))).toBe('12.ABC.345/01DE-35')
    expect(documentKind('12345678909')).toBe('PF')
    expect(documentKind('12ABC34501DE35')).toBe('PJ')
  })

  it('valor colado com pontuação e espaços é normalizado sem perder dígito nem duplicar a máscara', () => {
    expect(documentMask.normalize(' 47.225.005/0001-86 ')).toBe('47225005000186')
    expect(cepMask.format(cepMask.normalize('01310-100'))).toBe('01310-100')
    expect(ncmMask.format(ncmMask.normalize('8428.10.00'))).toBe('8428.10.00')
    expect(phoneMask.format(phoneMask.normalize('(11) 98765-4321'))).toBe('(11) 98765-4321')
    expect(phoneMask.format(phoneMask.normalize('1132654321'))).toBe('(11) 3265-4321')
  })

  it('dígitos verificadores de CPF e CNPJ (numérico e alfanumérico)', () => {
    expect(isValidCpf('123.456.789-09')).toBe(true)
    expect(isValidCpf('123.456.789-00')).toBe(false)
    expect(isValidCpf('111.111.111-11')).toBe(false)
    expect(isValidCnpj('47.225.005/0001-86')).toBe(true)
    expect(isValidCnpj('47.225.005/0001-87')).toBe(false)
    expect(isValidCnpj('12.ABC.345/01DE-35')).toBe(true)
    expect(isValidCnpj('12.ABC.345/01DE-36')).toBe(false)
  })
})

describe('números no padrão brasileiro (FND-09d CA 5)', () => {
  it('lê o que o usuário digita e entrega string normalizada', () => {
    expect(parseBrDecimal('1.234,56', 2)).toBe('1234.56')
    expect(parseBrDecimal('1234,5', 2)).toBe('1234.50')
    expect(parseBrDecimal('1234.5', 2)).toBe('1234.50')
    expect(parseBrDecimal('1.234', 2)).toBe('1234.00')
    expect(parseBrDecimal('R$ 0,005', 2)).toBe('0.01')
    expect(parseBrDecimal('-2,345', 2)).toBe('-2.35')
    expect(parseBrDecimal('33,33335', 4)).toBe('33.3334')
    expect(parseBrDecimal('abc', 2)).toBeNull()
    expect(parseBrDecimal('', 2)).toBeNull()
  })

  it('formata com milhar e casas do campo e compara sem float', () => {
    expect(formatBrDecimal('1234.5', 2)).toBe('1.234,50')
    expect(formatBrDecimal('1234567.891', 4)).toBe('1.234.567,8910')
    expect(formatBrDecimal('-0.5', 2)).toBe('-0,50')
    expect(compareDecimal('200.01', '200')).toBe(1)
    expect(compareDecimal('0.10', '0.1')).toBe(0)
  })
})
