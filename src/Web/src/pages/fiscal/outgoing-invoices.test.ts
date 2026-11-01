import { describe, expect, it } from 'vitest'
import { INVOICE_STATUS, invoiceLabel, isSlow } from './outgoing-invoices'

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
