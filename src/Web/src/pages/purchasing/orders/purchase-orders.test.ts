import { describe, expect, it } from 'vitest'
import { dispatchStatus, dispatchVerb, hasStranger, isResend, lateLabel, lineTotal, orderDestination, orderTotals, parseCc, projectedAfter } from './purchase-orders'

describe('envio ao fornecedor (CMP-03b)', () => {
  it('reenvio quando já foi enviado ou alterado depois do envio', () => {
    expect(isResend({ status: 'Approved', sentAt: null, changedAfterSent: false })).toBe(false)
    expect(isResend({ status: 'Sent', sentAt: '2026-10-24T10:00:00Z', changedAfterSent: false })).toBe(true)
    expect(isResend({ status: 'Approved', sentAt: '2026-10-24T10:00:00Z', changedAfterSent: true })).toBe(true)
    expect(dispatchVerb(false)).toMatchObject({ verb: 'Enviar ao fornecedor', done: 'Pedido enviado para a fila de e-mail' })
    expect(dispatchVerb(true)).toMatchObject({ verb: 'Reenviar', done: 'Pedido reenviado para a fila de e-mail' })
  })

  it('cópias e destinatário fora dos contatos', () => {
    expect(parseCc('a@x.com, b@x.com;c@x.com  d@x.com')).toEqual(['a@x.com', 'b@x.com', 'c@x.com', 'd@x.com'])
    expect(parseCc('')).toEqual([])
    expect(hasStranger(['Compras@Gama.com.br'], ['compras@gama.com.br'])).toBe(false)
    expect(hasStranger(['compras@gama.com.br', 'dono@gmail.com'], ['compras@gama.com.br'])).toBe(true)
  })

  it('situação do e-mail', () => {
    expect(dispatchStatus({ status: 'Queued', waitingScan: true }).label).toBe('Aguardando verificação do anexo')
    expect(dispatchStatus({ status: 'Queued', waitingScan: false }).label).toBe('Na fila')
    expect(dispatchStatus({ status: 'Sent', waitingScan: false })).toEqual({ label: 'Enviado', tone: 'ok' })
    expect(dispatchStatus({ status: 'Failed', error: 'O antivírus recusou.', waitingScan: false })).toEqual({ label: 'Falhou: O antivírus recusou.', tone: 'crit' })
  })
})

describe('rodapé do pedido (CMP-03a CA 3)', () => {
  it('linha com 2 casas, metade para longe do zero, como o servidor', () => {
    expect(lineTotal(3, 1.005)).toBe(3.02)
    expect(lineTotal(2.5, 10.123456)).toBe(25.31)
    expect(lineTotal(0.3333, 3)).toBe(1)
  })

  it('total = subtotal + frete + IPI sobre o subtotal', () => {
    expect(orderTotals([{ quantity: 10, unitPrice: 12.345 }, { quantity: 3, unitPrice: 1.005 }], 150, 5)).toEqual({ subtotal: 126.47, ipi: 6.32, total: 282.79 })
    expect(orderTotals([], 0, 0)).toEqual({ subtotal: 0, ipi: 0, total: 0 })
  })
})

describe('textos da lista (CA 20)', () => {
  it('atraso e destino', () => {
    expect(lateLabel(3)).toBe('Atrasado 3 d')
    expect(lateLabel(null)).toBeNull()
    expect(lateLabel(0)).toBeNull()
    expect(orderDestination(null)).toBe('Estoque geral')
    expect(orderDestination('PRJ-2026-0007', 'Aurora')).toBe('PRJ-2026-0007 — Aurora')
  })
})

describe('impacto no caixa (CA 12)', () => {
  it('desconta as parcelas acumuladas do saldo projetado de cada dia', () => {
    const balances: Record<string, number> = { '2026-12-01': 10_000, '2026-12-31': 8_000 }
    const rows = projectedAfter([{ estimatedDueDate: '2026-12-01', amount: 1_500 }, { estimatedDueDate: '2026-12-31', amount: 1_500 }], (d) => balances[d] ?? null)
    expect(rows.map((r) => r.after)).toEqual([8_500, 5_000])
    expect(projectedAfter([{ estimatedDueDate: '2027-02-01', amount: 10 }], () => null)[0].after).toBeNull()
  })
})
