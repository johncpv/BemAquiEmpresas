import { describe, expect, it } from 'vitest'
import { DIVERGENCE_KINDS, KIND_LABEL, resolutionsFor, todayInBrazil } from './pos-pending'

/** OFF-02 CA 15: a resolução que cada pendência aceita e os rótulos de cada tipo (os da ajuda). */
describe('Pendências do PDV', () => {
  it('o que já entrou só se aceita; a quarentena se libera ou fica fora', () => {
    expect(resolutionsFor(false)).toEqual(['AcceptAsIs'])
    expect(resolutionsFor(true)).toEqual(['ReleaseQuarantine', 'KeepOut'])
  })

  it('todo tipo tem rótulo e entra nos chips', () => {
    expect(DIVERGENCE_KINDS).toHaveLength(Object.keys(KIND_LABEL).length)
    expect(KIND_LABEL.PriceOutdated).toBe('Preço desatualizado')
    expect(KIND_LABEL.BrokenSequence).toBe('Sequência quebrada')
  })

  it('hoje é a data de Brasília', () => {
    expect(todayInBrazil(new Date('2026-11-20T02:30:00Z'))).toBe('2026-11-19')
  })
})
