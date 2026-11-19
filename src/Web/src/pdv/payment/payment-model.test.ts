import { describe, expect, it } from 'vitest'
import type { CatalogPaymentMethod } from '@/pdv/local-store/catalog-types'
import {
  allowsChange, brandsOf, canTransition, checkNsu, checkTenderNote, limitToRemaining, looksLikeCardNumber, methodAvailability, ONLY_CASH_GIVES_CHANGE,
  paidCents, parseMoney, quickCashOptions, remainingCents, salePaymentState, splitCash, type Tender,
} from './payment-model'
import { ManualTerminal, tenderFrom } from './payment-terminal'

const method = (kind: string, extra: Partial<CatalogPaymentMethod> = {}): CatalogPaymentMethod => ({ id: kind, name: kind, kind, ...extra })
const at = '2026-11-18T12:00:00Z'
const captured = (amountCents: number, kind = 'Pix'): Tender => tenderFrom({ tenderId: `t-${amountCents}-${kind}`, method: method(kind), amountCents, operatorId: 'op', at }, 'Manual', 'Captured')

describe('troco (CA 2 e 3)', () => {
  it('dinheiro: o pagamento é o que faltava e o resto é troco', () => {
    expect(splitCash(5_000, 1_899)).toEqual({ amountCents: 1_899, changeCents: 3_101 })
    expect(splitCash(1_000, 1_899)).toEqual({ amountCents: 1_000, changeCents: 0 })
    expect(splitCash(1_899, 1_899)).toEqual({ amountCents: 1_899, changeCents: 0 })
  })

  it('fora do dinheiro o valor é limitado ao que falta, com o aviso', () => {
    expect(limitToRemaining(2_000, 1_899)).toEqual({ valueCents: 1_899, message: ONLY_CASH_GIVES_CHANGE })
    expect(limitToRemaining(1_000, 1_899)).toEqual({ valueCents: 1_000, message: null })
    expect(ONLY_CASH_GIVES_CHANGE).toBe('Só o dinheiro dá troco.')
  })

  it('só o dinheiro dá troco (catálogo antigo sem o campo vale pelo tipo)', () => {
    expect(allowsChange(method('Cash'))).toBe(true)
    expect(allowsChange(method('Pix'))).toBe(false)
    expect(allowsChange(method('Cash', { allowsChange: false }))).toBe(false)
  })

  it('valores rápidos: o exato e as notas acima', () => {
    expect(quickCashOptions(1_899)).toEqual([1_899, 2_000, 5_000, 10_000])
    expect(quickCashOptions(5_000)).toEqual([5_000, 6_000, 10_000, 20_000])
    expect(quickCashOptions(0)).toEqual([])
  })

  it('o troco do dinheiro sai do aparelho com o valor da venda e o recebido à parte', () => {
    const t = tenderFrom({ tenderId: 't', method: method('Cash'), amountCents: 1_899, receivedCents: 5_000, operatorId: 'op', at }, 'Manual', 'Captured')
    expect(t).toMatchObject({ amountCents: 1_899, receivedCents: 5_000, changeCents: 3_101, cardBrand: null, installments: null })
  })

  it('lê o valor digitado em reais', () => {
    expect(parseMoney('18,99')).toBe(1_899)
    expect(parseMoney('1.234,50')).toBe(123_450)
    expect(parseMoney('R$ 20')).toBe(2_000)
    expect(parseMoney('20.5')).toBe(2_050)
    expect(parseMoney('abc')).toBeNull()
    expect(parseMoney('')).toBeNull()
    expect(parseMoney('1,999')).toBeNull()
  })
})

describe('invariante da soma (centavos, 1 a 3 formas)', () => {
  it('soma só os capturados e o que falta nunca fica negativo', () => {
    expect(paidCents([captured(4_489)])).toBe(4_489)
    expect(remainingCents(4_489, [captured(4_489)])).toBe(0)
    const three = [captured(1_001, 'Pix'), captured(2_000, 'Credit'), captured(1_488, 'Cash')]
    expect(paidCents(three)).toBe(4_489)
    expect(remainingCents(4_489, three.slice(0, 2))).toBe(1_488)
    const failed = { ...captured(500), tenderId: 'f', status: 'Failed' as const }
    expect(paidCents([captured(1_000), failed])).toBe(1_000)
    // 0,1 + 0,2 em centavos é exato.
    expect(paidCents([captured(10), captured(20, 'Cash')])).toBe(30)
  })

  it('estado da venda: Open, InPayment, PartiallyPaid e Completed', () => {
    expect(salePaymentState(false, 4_489, [])).toBe('Open')
    expect(salePaymentState(true, 4_489, [])).toBe('InPayment')
    expect(salePaymentState(true, 4_489, [captured(1_001)])).toBe('PartiallyPaid')
    expect(salePaymentState(true, 4_489, [captured(1_001), captured(3_488, 'Cash')])).toBe('Completed')
  })
})

describe('máquina de estados do pagamento (CA 11)', () => {
  it('Created → Pending → Captured → Voided/Refunded, mais Failed e Unknown', () => {
    expect(canTransition('Created', 'Pending')).toBe(true)
    expect(canTransition('Pending', 'Unknown')).toBe(true)
    expect(canTransition('Unknown', 'Captured')).toBe(true)
    expect(canTransition('Unknown', 'Failed')).toBe(true)
    expect(canTransition('Captured', 'Refunded')).toBe(true)
    expect(canTransition('Captured', 'Voided')).toBe(true)
    // Capturado é fato: não volta nem falha.
    expect(canTransition('Captured', 'Failed')).toBe(false)
    expect(canTransition('Captured', 'Pending')).toBe(false)
    expect(canTransition('Failed', 'Captured')).toBe(false)
  })

  it('a maquininha externa captura na hora, atestado pelo operador; o Pix fica para conferir no extrato', async () => {
    const terminal = new ManualTerminal()
    const pix = await terminal.charge({ tenderId: 'p', method: method('Pix'), amountCents: 1_000, operatorId: 'op', at })
    expect(pix).toMatchObject({ status: 'Captured', integration: 'Manual', operatorAttested: true, needsReconciliation: true, confirmedBy: 'op' })
    const card = await terminal.charge({ tenderId: 'c', method: method('Credit'), amountCents: 1_000, cardBrand: 'Visa', installments: 3, nsu: '123456', operatorId: 'op', at })
    expect(card).toMatchObject({ status: 'Captured', cardBrand: 'Visa', installments: 3, nsu: '123456', needsReconciliation: false, changeCents: 0, receivedCents: null })
    // CA 10: todos os campos existem, vazios quando não se aplicam.
    expect(Object.keys(card).sort()).toEqual([
      'acquirerId', 'amountCents', 'at', 'authorizationCode', 'cardBrand', 'changeCents', 'confirmedBy', 'endToEndId', 'installments', 'integration',
      'methodId', 'methodName', 'methodType', 'needsReconciliation', 'note', 'nsu', 'operatorAttested', 'partId', 'provider', 'receivedCents', 'status',
      'tenderId', 'txid',
    ])
    const unknown = { ...card, status: 'Unknown' as const }
    expect((await terminal.check(unknown, true, at)).status).toBe('Captured')
    expect((await terminal.check(unknown, false, at)).status).toBe('Failed')
  })
})

describe('cartão sem dado de cartão (CA 4, segurança)', () => {
  it('recusa sequência que parece número de cartão no NSU e na observação', () => {
    expect(looksLikeCardNumber('4111 1111 1111 1111')).toBe(true)
    expect(looksLikeCardNumber('4111-1111-1111-1111')).toBe(true)
    expect(looksLikeCardNumber('5500000000000004')).toBe(true)
    expect(looksLikeCardNumber('123456789012')).toBe(false)
    expect(checkNsu('4111111111111111')).toEqual({ ok: false, message: 'Não digite o número do cartão.' })
    expect(checkTenderNote('cartão 4111 1111 1111 1111 do cliente')).toEqual({ ok: false, message: 'Não digite o número do cartão.' })
    expect(checkNsu(' 123456 ')).toEqual({ ok: true, value: '123456' })
    expect(checkNsu('')).toEqual({ ok: true, value: null })
    expect(checkNsu('12/34')).toMatchObject({ ok: false })
    expect(checkTenderNote('cliente pediu nota')).toEqual({ ok: true, value: 'cliente pediu nota' })
  })

  it('a bandeira vem da forma; sem bandeira própria, todas', () => {
    expect(brandsOf(method('Credit', { cardBrands: ['Visa', 'Elo'] }))).toEqual(['Visa', 'Elo'])
    expect(brandsOf(method('Credit'))).toContain('Mastercard')
  })
})

describe('vale-troca e fiado (CA 9)', () => {
  it('sem rede, desabilitados; com rede, ocultos até as stories deles', () => {
    expect(methodAvailability(method('StoreCredit'), false)).toBe('needs-network')
    expect(methodAvailability(method('OnAccount'), false)).toBe('needs-network')
    expect(methodAvailability(method('OnAccount'), true)).toBe('hidden')
    expect(methodAvailability(method('Cash'), false)).toBe('enabled')
    expect(methodAvailability(method('Pix'), true)).toBe('enabled')
  })
})
