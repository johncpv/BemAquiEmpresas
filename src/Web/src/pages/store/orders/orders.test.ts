import { describe, expect, it } from 'vitest'
import { draftTotals, isNextStep, nextStepLabel, shortLines, staleDays, statusChip, timelineLabel, type DraftLine } from './orders'

const line = (patch: Partial<DraftLine> = {}): DraftLine => ({ productId: 'p', description: 'Peça', unitCode: 'UN', price: 100, available: 5, quantity: 2, discount: 0, ...patch })

describe('pedido de venda (VND-02a)', () => {
  it('prévia do total com desconto de linha e do pedido', () => {
    expect(draftTotals([line({ discount: 10 }), line({ price: 33.33, quantity: 3 })], 5)).toEqual({ subtotal: 299.99, discounts: 15, total: 284.99 })
  })

  it('aponta a linha que pede mais do que o disponível', () => {
    expect(shortLines([line(), line({ quantity: 6, productId: 'x' })]).map((l) => l.productId)).toEqual(['x'])
  })

  it('chip, próximo passo e linha do tempo com texto', () => {
    expect(statusChip('AwaitingPayment')).toEqual({ label: 'Aguardando pagamento', tone: 'warn' })
    expect(statusChip('Outro').label).toBe('Outro')
    expect(nextStepLabel('Charge')).toBe('Gerar cobrança')
    expect(isNextStep('Confirm')).toBe(true)
    expect(isNextStep('x')).toBe(false)
    expect(timelineLabel('cancel_paid')).toBe('Pedido cancelado e venda estornada')
  })

  it('dias parado', () => {
    expect(staleDays('2026-11-20T12:00:00Z', new Date('2026-11-27T13:00:00Z'))).toBe(7)
  })
})
