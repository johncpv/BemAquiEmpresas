import { describe, expect, it } from 'vitest'
import { emptyMethod, payableSituation, retentionSum, toDetails } from './payables'

describe('títulos a pagar (FIN-02)', () => {
  const base = { status: 'Open' as const, isOverdue: false, isDueToday: false }
  it('situação: vencido calculado, em OP e aprovado', () => {
    expect(payableSituation({ ...base, isOverdue: true }).label).toBe('Vencido')
    expect(payableSituation({ ...base, status: 'AwaitingApproval' }).label).toBe('Em OP')
    expect(payableSituation({ ...base, status: 'Approved' })).toEqual({ label: 'Aprovado', tone: 'ok' })
    expect(payableSituation({ ...base, status: 'Paid', isOverdue: true }).label).toBe('Pago')
  })
  it('forma de pagamento: só os campos do tipo vão para a API', () => {
    expect(toDetails(emptyMethod())).toBeNull()
    expect(toDetails({ ...emptyMethod(), kind: 'Boleto', barcode: ' 123 ', pixKey: 'x' })).toEqual({ kind: 'Boleto', barcode: '123' })
    expect(toDetails({ ...emptyMethod(), kind: 'Pix', pixKeyType: 'Email', pixKey: 'a@b.com', holderName: 'A', holderDocument: '1' })).toMatchObject({ kind: 'Pix', pixKeyType: 'Email', pixKey: 'a@b.com' })
  })
  it('soma das retenções', () => {
    expect(retentionSum({ iss: '50', inss: null, ir: '10.5', pcc: '' })).toBe(60.5)
  })
})
