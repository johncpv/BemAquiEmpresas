import { describe, expect, it } from 'vitest'
import type { OutboxDraft } from '@/pdv/local-store/local-store'
import type { HardwareBridge, PrinterTarget } from './hardware-bridge'
import { PRINT_OUTBOX_KIND, printersOf, printSaleReceipt, rememberReceipt, type StoredReceipt, targetOf } from './print-jobs'
import type { ReceiptDocument } from './receipt-model'

/** Só o que a impressão usa do `LocalStore`: meta e a fila (na mesma "transação"). */
function fakeStore() {
  const meta = new Map<string, unknown>()
  const outbox: OutboxDraft[] = []
  return {
    outbox,
    getMeta: async <T,>(key: string) => meta.get(key) as T | undefined,
    updateMeta: async (key: string, update: (current: unknown) => unknown, entry?: OutboxDraft | ((n: unknown, p: unknown) => OutboxDraft | null)) => {
      const previous = meta.get(key)
      const next = update(previous)
      meta.set(key, next)
      const draft = typeof entry === 'function' ? entry(next, previous) : entry
      if (draft) outbox.push(draft)
      return null
    },
  }
}

function fakeBridge(fail = false) {
  const printed: { doc: ReceiptDocument; printer: PrinterTarget }[] = []
  const bridge: HardwareBridge = { kind: 'browser', print: async (doc, printer) => { if (fail) return { ok: false, message: 'Não foi possível imprimir.' }; printed.push({ doc, printer }); return { ok: true } } }
  return { bridge, printed }
}

const receipt: StoredReceipt = {
  saleId: 's1', number: 'LOJA-01.1-000001', completedAt: '2026-11-19T15:30:00Z', operatorId: 'op1', operatorName: 'Caio O.',
  items: [{ name: 'Café', quantity: '1', unit: 'UN', fractional: false, unitCents: 1_290 }], totalCents: 1_290, changeCents: 0,
  payments: [{ label: 'Pix', amountCents: 1_290 }], customerName: null, documentId: 'doc1', documentKind: 'cpf', copies: 0,
}
const header = { companyName: 'Mercadinho', companyDocument: '12345678000195', storeName: 'Loja' }
const printer: PrinterTarget = { id: 'p1', name: 'Bobina', paperWidthMm: 80, columns: 48 }

describe('impressão do cupom (IMP-01)', () => {
  it('CA 4: a primeira via é o original; a próxima sai como 2ª via e vai para a fila com quem e quando', async () => {
    const store = fakeStore()
    const { bridge, printed } = fakeBridge()
    await rememberReceipt(store, receipt)
    const options = { store, bridge, printer, saleId: 's1', header, operatorId: 'op2', registerId: 'r1', decryptDocument: async () => '52998224725', now: () => new Date('2026-11-19T16:00:00Z') }

    expect(await printSaleReceipt(options)).toEqual({ ok: true, copy: 1 })
    expect(await printSaleReceipt(options)).toEqual({ ok: true, copy: 2 })
    expect(printed[1].doc.title).toContain('2ª VIA')
    expect(store.outbox.map((e) => e.kind)).toEqual([PRINT_OUTBOX_KIND, PRINT_OUTBOX_KIND])
    expect(store.outbox[1].payload).toMatchObject({ type: 'ReceiptReprinted', kind: 'Reprint', copy: 2, saleId: 's1', operatorId: 'op2', printerId: 'p1', registerId: 'r1', printedAt: '2026-11-19T16:00:00.000Z' })
    // PP-26: o CPF sai mascarado no papel e nunca vai para a fila.
    expect(JSON.stringify(printed[0].doc)).toContain('***.982.247-**')
    expect(JSON.stringify(store.outbox)).not.toContain('52998224725')
  })

  it('CA 8: a falha da impressora não conta via nem põe evento na fila (a venda não muda)', async () => {
    const store = fakeStore()
    await rememberReceipt(store, receipt)
    const result = await printSaleReceipt({ store, bridge: fakeBridge(true).bridge, printer, saleId: 's1', header, operatorId: 'op1', registerId: 'r1' })
    expect(result).toEqual({ ok: false, message: 'Não foi possível imprimir.' })
    expect(store.outbox).toEqual([])
    const ok = await printSaleReceipt({ store, bridge: fakeBridge().bridge, printer, saleId: 's1', header, operatorId: 'op1', registerId: 'r1' })
    expect(ok).toEqual({ ok: true, copy: 1 })
  })

  it('CA 3 e 7: a impressora do cadastro dita as colunas; sem cadastro, a do navegador com 42; a padrão vem primeiro', () => {
    expect(targetOf({ id: 'a', name: 'A', kind: 'printer', paperWidthMm: 58, columns: 32 })).toEqual({ id: 'a', name: 'A', paperWidthMm: 58, columns: 32 })
    expect(targetOf(null).columns).toBe(42)
    const list = printersOf([{ id: 'a', name: 'A', kind: 'printer', isDefault: false }, { id: 'b', name: 'B', kind: 'printer', isDefault: true }], { id: 'c', name: 'C', kind: 'printer', isDefault: false })
    expect(list.map((p) => p.id)).toEqual(['b', 'c', 'a'])
  })
})
