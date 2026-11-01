import { describe, expect, it } from 'vitest'
import { decimalField, isNfseSlow, nfseLabel } from './nfse'

describe('NFS-e da instalação (FIS-06)', () => {
  it('rótulo pelo número da prefeitura', () => {
    expect(nfseLabel(9001)).toBe('NFS-e 9001')
    expect(nfseLabel(null)).toBe('Rascunho de NFS-e')
  })

  it('valor do registro com ponto decimal, do campo numérico ou digitado em pt-BR', () => {
    expect(decimalField('10000.5')).toBe('10000.50')
    expect(decimalField('10.000,50')).toBe('10000.50')
    expect(decimalField('')).toBe('')
    expect(decimalField('abc')).toBe('')
  })

  it('demorando depois de 30 s em processamento', () => {
    const now = Date.parse('2026-11-01T12:00:00Z')
    expect(isNfseSlow('Processing', '2026-11-01T11:59:00Z', now)).toBe(true)
    expect(isNfseSlow('Processing', '2026-11-01T11:59:50Z', now)).toBe(false)
    expect(isNfseSlow('Authorized', '2026-11-01T11:00:00Z', now)).toBe(false)
  })
})
