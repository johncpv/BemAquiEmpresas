import { describe, expect, it } from 'vitest'
import { chooseAll, idsFromSearch, initialSelection, missingForClose, needsReason, responseState, responseTotal, toStockPrice } from './rfqs'

const lines = [
  { id: 'a', itemId: 'i1', itemCode: 'MP-010', quantity: 100, selectedSupplierId: null, cheapestSupplierId: 's2' },
  { id: 'b', itemId: 'i2', itemCode: 'MP-020', quantity: 10, selectedSupplierId: null, cheapestSupplierId: 's3' },
  { id: 'c', itemId: 'i3', itemCode: 'MP-030', quantity: 1, selectedSupplierId: null, cheapestSupplierId: null },
]

describe('cotação (CMP-02)', () => {
  it('pré-marca o menor custo e respeita o vencedor já gravado', () => {
    expect(initialSelection(lines)).toEqual({ a: 's2', b: 's3' })
    expect(initialSelection([{ ...lines[0], selectedSupplierId: 's1' }])).toEqual({ a: 's1' })
  })

  it('"Escolher tudo de" troca só as linhas que o fornecedor cotou', () => {
    const next = chooseAll({ a: 's2', b: 's3' }, { id: 's1', quotes: [{ lineId: 'a' }] as never })
    expect(next).toEqual({ a: 's1', b: 's3' })
  })

  it('motivo só quando o vencedor não é o de menor custo', () => {
    expect(needsReason(lines[0], 's2')).toBe(false)
    expect(needsReason(lines[0], 's1')).toBe(true)
    expect(needsReason(lines[2], 's1')).toBe(false)
  })

  it('diz o que falta para concluir: vencedor e motivo, Outro com o texto', () => {
    const quoted = new Set(['a', 'b'])
    expect(missingForClose(lines, quoted, { a: 's1' }, {})).toEqual(['Informe o motivo da escolha de MP-010.', 'Escolha o vencedor de MP-020.'])
    expect(missingForClose(lines, quoted, { a: 's1', b: 's3' }, { a: { reason: 'Other', note: ' ' } })).toEqual(['Escreva o motivo da escolha de MP-010.'])
    expect(missingForClose(lines, quoted, { a: 's1', b: 's3' }, { a: { reason: 'LeadTime', note: '' } })).toEqual([])
  })

  it('converte o preço da unidade do fornecedor como o servidor (6 casas)', () => {
    expect(toStockPrice(100, 50)).toBe(2)
    expect(toStockPrice(10, 3)).toBe(3.333333)
    expect(toStockPrice(10, 0)).toBe(0)
  })

  it('custo total ao vivo com frete, IPI e a unidade do fornecedor', () => {
    const quotes = { a: { price: '100', inSupplierUnit: true, available: null, leadDays: '5' }, b: { price: '50', inSupplierUnit: false, available: null, leadDays: '5' } }
    expect(responseTotal(lines, quotes, [{ itemId: 'i1', factor: 50 }], 20, 10)).toEqual({ subtotal: 700, ipi: 70, total: 790 })
  })

  it('situação da resposta e os ids da URL', () => {
    expect(responseState({ responded: true, declined: false }).label).toBe('Respondida')
    expect(responseState({ responded: false, declined: true }).label).toBe('Não cotou')
    expect(responseState({ responded: false, declined: false }).tone).toBe('warn')
    expect(idsFromSearch('a, b,,c')).toEqual(['a', 'b', 'c'])
    expect(idsFromSearch(undefined)).toEqual([])
  })
})
