import { describe, expect, it } from 'vitest'
import { newRuleValues, parseList, suggestInterstateCfop, switchOperation } from './tax-rules'

describe('regras fiscais (FIS-02b)', () => {
  it('sugere o CFOP fora do estado a partir do de dentro', () => {
    expect(suggestInterstateCfop('5101')).toBe('6101')
    expect(suggestInterstateCfop('6101')).toBeNull()
    expect(suggestInterstateCfop('51')).toBeNull()
  })

  it('prestação de serviço tira CFOP, ICMS, IPI e DIFAL e põe o ISS; venda volta com o ICMS', () => {
    const service = switchOperation(newRuleValues('2026-10-31'), 'ServiceProvision')
    expect(service.icms).toBeNull()
    expect(service.ipi).toBeNull()
    expect(service.cfopInternal).toBeNull()
    expect(service.iss).toMatchObject({ serviceItem: '7.02', rate: 5 })
    const sale = switchOperation(service, 'Resale')
    expect(sale.iss).toBeNull()
    expect(sale.icms?.internalRate).toBe(18)
  })

  it('lista de UFs e de NCM', () => {
    expect(parseList('sp, mg;RJ')).toEqual(['SP', 'MG', 'RJ'])
    expect(parseList('8428.10, 7308', true)).toEqual(['842810', '7308'])
  })
})
