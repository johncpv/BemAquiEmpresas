import { describe, expect, it } from 'vitest'
import { correctionWarnings, EVENT_STATUS, INVOICE_STATUS, invoiceLabel, isSlow, rangeNumbers } from './outgoing-invoices'

describe('nota de saída (FIS-03a)', () => {
  it('situações com a cor semântica da story (CA 17)', () => {
    expect(INVOICE_STATUS.Authorized.tone).toBe('ok')
    expect(INVOICE_STATUS.Rejected.tone).toBe('crit')
    expect(INVOICE_STATUS.Denied.tone).toBe('crit')
    expect(INVOICE_STATUS.ContingencyPending.tone).toBe('warn')
    expect(INVOICE_STATUS.Processing.tone).toBe('info')
    expect(INVOICE_STATUS.Draft.tone).toBe('neutral')
  })

  it('"Demorando" depois de 30 s processando ou em contingência (CA 10)', () => {
    const now = Date.parse('2026-10-31T12:00:40Z')
    expect(isSlow('Processing', '2026-10-31T12:00:00Z', now)).toBe(true)
    expect(isSlow('Processing', '2026-10-31T12:00:20Z', now)).toBe(false)
    expect(isSlow('ContingencyPending', null, now)).toBe(true)
    expect(isSlow('Authorized', '2026-10-31T11:00:00Z', now)).toBe(false)
  })

  it('rótulo com série e número', () => {
    expect(invoiceLabel(2, 42)).toBe('NF-e 2/42')
    expect(invoiceLabel(null, null)).toBe('Rascunho de nota')
  })
})

describe('cancelamento, carta e inutilização (FIS-03b)', () => {
  it('a carta avisa (sem bloquear) valor em R$, CNPJ/CPF e data (CA 9)', () => {
    expect(correctionWarnings('Corrige o volume transportado: 3 volumes em vez de 2, peso 851 kg.')).toEqual([])
    expect(correctionWarnings('O valor correto é R$ 1.250,00')).toHaveLength(1)
    expect(correctionWarnings('Valor 12.500,00 do item')).toHaveLength(1)
    expect(correctionWarnings('Destinatário 11.222.333/0001-81')[0]).toContain('CNPJ ou CPF')
    expect(correctionWarnings('CPF 123.456.789-09')[0]).toContain('CNPJ ou CPF')
    expect(correctionWarnings('Emissão em 31/10/2026')[0]).toContain('data')
  })

  it('faixa vira a lista do impacto, com limite', () => {
    expect(rangeNumbers(10, 12)).toEqual([10, 11, 12])
    expect(rangeNumbers(1, 500)).toHaveLength(50)
  })

  it('situações do evento com a cor semântica', () => {
    expect(EVENT_STATUS.Registered.tone).toBe('ok')
    expect(EVENT_STATUS.Rejected.tone).toBe('crit')
    expect(EVENT_STATUS.Requested.label).toBe('Enviado')
  })
})
