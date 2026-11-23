import { describe, expect, it } from 'vitest'
import type { OutboxEntry } from '@/pdv/local-store/local-store'
import { checkStock, mergeStock, noStockMessage, pendingSold } from './stock-levels'

const sale = (seq: number, events: unknown[], status = 'completed'): OutboxEntry =>
  ({ seq, id: `e${seq}`, kind: 'pos.sale', createdAt: '2026-11-23T12:00:00.000Z', payload: { status, events } })

describe('saldo da loja no caixa (PDV-05)', () => {
  it('a lista inteira troca a do aparelho e o delta escreve por cima', () => {
    const full = mergeStock(null, { sellWithoutStock: false, full: true, cursor: 10, levels: [{ productId: 'a', available: 2 }, { productId: 'b', available: 0.5 }] }, 'x')
    expect(full.levels).toEqual({ a: 2000, b: 500 })
    const delta = mergeStock(full, { sellWithoutStock: true, full: false, cursor: 20, levels: [{ productId: 'a', available: 1 }] }, 'y')
    expect(delta).toMatchObject({ sellWithoutStock: true, cursor: 20, levels: { a: 1000, b: 500 } })
  })

  it('desconta as vendas da fila de saída e recusa acima do disponível, só com a opção desligada', () => {
    const outbox = [
      sale(1, [{ type: 'LineAdded', data: { line: { lineId: 'l1', itemId: 'a', quantity: '1' } } }, { type: 'LineChanged', data: { lineId: 'l1', quantity: '2' } }]),
      sale(2, [{ type: 'LineAdded', data: { line: { lineId: 'l2', itemId: 'a', quantity: '5' } } }], 'discarded'),
    ]
    const pending = pendingSold(outbox)
    expect(pending).toEqual({ a: 2000 })
    const stock = mergeStock(null, { sellWithoutStock: false, full: true, cursor: 1, levels: [{ productId: 'a', available: 3 }] }, 'x')
    expect(checkStock(stock, 'a', 1000, pending)).toEqual({ ok: true })
    expect(checkStock(stock, 'a', 2000, pending)).toEqual({ ok: false, availableMilli: 1000 })
    expect(checkStock({ ...stock, sellWithoutStock: true }, 'a', 9000, pending)).toEqual({ ok: true })
    expect(checkStock(stock, 'sem-saldo-recebido', 9000, pending)).toEqual({ ok: true })
    expect(noStockMessage('Arroz', 1000, 'UN')).toBe('Arroz: só 1 UN em saldo nesta loja. A empresa não vende item sem saldo.')
  })
})
