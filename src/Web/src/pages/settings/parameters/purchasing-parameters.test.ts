import { describe, expect, it } from 'vitest'
import { accountingPending, costTypeLabel, parseTolerance, purchasingChanged, type PurchasingAccounting } from './purchasing-parameters'

const saved: PurchasingAccounting = { defaultLeadDays: 15, payableAccountCode: '2.1', payableCostType: 'Material', freightAccountCode: '2.4', freightCostType: 'Freight', accountingConfirmed: false }

describe('Compras em Configurações → Parâmetros (CMP-03a)', () => {
  it('habilita salvar só quando algo muda', () => {
    expect(purchasingChanged(saved, { ...saved })).toBe(false)
    expect(purchasingChanged({ ...saved, accounts: [] } as PurchasingAccounting, { ...saved }), 'campos de leitura (contas, id) não contam').toBe(false)
    expect(purchasingChanged(saved, { ...saved, payableAccountCode: '1.3' })).toBe(true)
    expect(purchasingChanged(saved, { ...saved, accountingConfirmed: true })).toBe(true)
  })

  it('selo "A confirmar com a contadora" até a conferência e rótulos das naturezas', () => {
    expect(accountingPending(saved)).toBe(true)
    expect(accountingPending({ accountingConfirmed: true })).toBe(false)
    expect(costTypeLabel('Freight')).toBe('Frete')
    expect(costTypeLabel('Material')).toBe('Material')
  })

  it('tolerância do recebimento de 0% a 50% (CMP-04) e a mudança dela habilita salvar', () => {
    expect(parseTolerance('0')).toBe(0)
    expect(parseTolerance('2,5')).toBe(2.5)
    expect(parseTolerance('50')).toBe(50)
    expect(parseTolerance('50,01')).toBeNull()
    expect(parseTolerance('-1')).toBeNull()
    expect(parseTolerance('1,234')).toBeNull()
    expect(parseTolerance('')).toBeNull()
    expect(purchasingChanged(saved, { ...saved, receiptTolerancePct: 0 }), 'sem tolerância salva vale 0').toBe(false)
    expect(purchasingChanged(saved, { ...saved, receiptTolerancePct: 5 })).toBe(true)
  })
})
