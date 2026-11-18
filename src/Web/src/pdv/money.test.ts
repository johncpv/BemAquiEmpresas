import { describe, expect, it } from 'vitest'
import { centsToDecimal, formatCents, lineTotalCents, MoneyError, sumCents, toCents } from './money'
import { isUuidV7, uuidv7, uuidv7Time } from './uuid-v7'

/** OFF-01 CA 8: dinheiro em centavos inteiros; ids Guid v7 gerados no aparelho. */
describe('dinheiro em centavos', () => {
  it.each([
    ['12.34', 1234], ['12,34', 1234], ['0.1', 10], ['0.05', 5], ['1234', 123400], ['-0.5', -50], ['0', 0], [19.9, 1990], [0.07, 7], [-3, -300],
  ] as const)('%s vira %i centavos', (value, cents) => expect(toCents(value)).toBe(cents))

  it.each(['12.345', 'abc', '', '1.2.3', 'R$ 10'])('recusa "%s" (mais de 2 casas ou texto inválido)', (value) => expect(() => toCents(value)).toThrow(MoneyError))

  it('recusa número que não cabe em 2 casas, sem arredondar escondido', () => {
    expect(() => toCents(0.1 + 0.2)).toThrow(MoneyError)
    expect(() => toCents(Number.NaN)).toThrow(MoneyError)
  })

  it('soma em centavos não sofre com ponto flutuante', () => {
    expect(sumCents([toCents('0.1'), toCents('0.2')])).toBe(30)
    expect(0.1 + 0.2).not.toBe(0.3)
    expect(() => sumCents([1, 0.5])).toThrow(MoneyError)
  })

  it('volta para o decimal da API', () => {
    expect(centsToDecimal(1234)).toBe('12.34')
    expect(centsToDecimal(5)).toBe('0.05')
    expect(centsToDecimal(-50)).toBe('-0.50')
    expect(() => centsToDecimal(1.5)).toThrow(MoneyError)
  })

  it('linha = preço × quantidade (até 4 casas), arredondando meio para longe do zero como o servidor', () => {
    expect(lineTotalCents(1990, 3)).toBe(5970)
    expect(lineTotalCents(1999, '0.5')).toBe(1000) // 999,5 -> 1000
    expect(lineTotalCents(1000, '1.2345')).toBe(1235) // 1234,5 -> 1235
    expect(lineTotalCents(333, '0.333')).toBe(111) // 110,889 -> 111
    expect(lineTotalCents(-1999, '0.5')).toBe(-1000)
    expect(() => lineTotalCents(100, '1.23456')).toThrow(MoneyError)
  })

  it('formata para a tela em reais', () => {
    expect(formatCents(123456).replace(/\s/g, ' ')).toBe('R$ 1.234,56')
  })
})

describe('Guid v7 do aparelho', () => {
  it('tem versão 7, variante RFC 9562 e o instante na frente', () => {
    const at = Date.UTC(2026, 10, 18, 12, 0, 0)
    const id = uuidv7(at)
    expect(isUuidV7(id)).toBe(true)
    expect(uuidv7Time(id)).toBe(at)
  })

  it('ordena pelo instante e não repete', () => {
    const a = uuidv7(1_000)
    const b = uuidv7(2_000)
    expect(a < b).toBe(true)
    const many = new Set(Array.from({ length: 1000 }, () => uuidv7(5_000)))
    expect(many.size).toBe(1000)
  })

  it('usa os bytes aleatórios dados (o resto do id)', () => {
    const id = uuidv7(0, (b) => b.fill(0xff))
    expect(id).toBe('00000000-0000-7fff-bfff-ffffffffffff')
  })
})
