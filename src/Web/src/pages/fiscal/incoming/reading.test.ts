import { describe, expect, it } from 'vitest'
import {
  firstToCheck, isValidAccessKey, parseNumber, pendingCount, pendingLabel, precheckPdf, removeConfirmed, removeItem, requiredKeys, STATE_LABEL, sumMismatch, toInput, toRequest,
  type FieldState,
} from './reading'

describe('leitura assistida (FIS-05)', () => {
  it('conta os campos a conferir e põe o foco no primeiro incerto ou não encontrado (CA 7)', () => {
    const keys = requiredKeys(1)
    expect(keys).toHaveLength(22)
    expect(keys[0]).toBe('accessKey')
    expect(keys.at(-1)).toBe('items.0.amount')
    const states: Record<string, FieldState> = Object.fromEntries(keys.map((k) => [k, 'Read' as FieldState]))
    states['totals.total'] = 'Uncertain'
    states['items.0.ncm'] = 'NotFound'
    const confirmed = new Set<string>()
    expect(firstToCheck(keys, states, confirmed)).toBe('totals.total')
    confirmed.add('totals.total')
    expect(firstToCheck(keys, states, confirmed)).toBe('items.0.ncm')
    confirmed.add('items.0.ncm')
    expect(firstToCheck(keys, states, confirmed)).toBe('accessKey')
    expect(pendingCount(keys, confirmed)).toBe(20)
    expect(pendingLabel(20)).toBe('20 campos a conferir')
    expect(pendingLabel(1)).toBe('1 campo a conferir')
    expect(pendingLabel(0)).toBe('Todos os campos conferidos')
    expect(STATE_LABEL.Uncertain.label).toBe('Leitura incerta: confira')
    expect(STATE_LABEL.NotFound.label).toBe('Não encontrado')
  })

  it('soma dos itens contra o total dos produtos (CA 8)', () => {
    const values = { 'totals.products': '145,00', 'items.0.amount': '125,00', 'items.1.amount': '20' }
    expect(sumMismatch(values, 2)).toBeNull()
    expect(sumMismatch({ ...values, 'totals.products': '1.200,00' }, 2)).toMatch(/^A soma dos itens \(R\$\s145,00\) não bate com o total da nota \(R\$\s1\.200,00\)\.$/)
  })

  it('números com vírgula, chave com DV e o corpo do Conferir nota', () => {
    expect(parseNumber('1.234,56')).toBe(1234.56)
    expect(parseNumber('12,5')).toBe(12.5)
    expect(parseNumber('10')).toBe(10)
    expect(parseNumber('10.5')).toBe(10.5)
    expect(parseNumber('abc')).toBeNull()
    expect(toInput('money', '1234.5')).toBe('1234,5')
    expect(isValidAccessKey('3526 1047 2250 0500 0186 5500 1000 0012 3411 2345 6785')).toBe(isValidAccessKey('35261047225005000186550010000012341123456785'))
    expect(isValidAccessKey('123')).toBe(false)

    const keys = requiredKeys(1)
    const request = toRequest({ accessKey: '3526 1047', issuedAt: '2026-10-15', 'totals.products': '125,00', 'totals.total': '125,00', 'items.0.quantity': '10', 'items.0.ncm': '7208.51.00' }, 1, new Set(keys.slice(0, 3)))
    expect(request).toMatchObject({ accessKey: '35261047', issuedOn: '2026-10-15', totals: { products: 125, freight: 0, total: 125 }, items: [{ quantity: 10, ncm: '72085100', code: null }] })
    expect(request.confirmed).toEqual(keys.slice(0, 3))
  })

  it('tirar um item renumera os campos e a conferência', () => {
    const values = { 'totals.total': '1', 'items.0.code': 'A', 'items.1.code': 'B', 'items.2.code': 'C' }
    expect(removeItem(values, 1, 3)).toEqual({ 'totals.total': '1', 'items.0.code': 'A', 'items.1.code': 'C' })
    expect([...removeConfirmed(new Set(['items.0.code', 'items.2.code', 'accessKey']), 0, 3)].sort()).toEqual(['accessKey', 'items.1.code'])
  })

  it('confere o PDF antes de enviar', () => {
    expect(precheckPdf({ name: 'danfe.PDF', size: 10 })).toBeNull()
    expect(precheckPdf({ name: 'nota.xml', size: 10 })).toBe('Envie o PDF da DANFE (arquivo .pdf).')
    expect(precheckPdf({ name: 'danfe.pdf', size: 6 * 1024 * 1024 })).toBe('O PDF precisa ter até 5 MB.')
  })
})
