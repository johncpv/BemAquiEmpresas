import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it, vi } from 'vitest'
import { emptyCatalog, type CatalogPaymentMethod } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import type { OutboxEntry } from '@/pdv/local-store/local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import type { Tender } from '@/pdv/payment/payment-model'
import { ManualTerminal, tenderFrom } from '@/pdv/payment/payment-terminal'
import { aesDocumentCipher } from './customer-document'
import { saleTotal } from './sale-model'
import { DEFAULT_SETTINGS, LOCKED_MESSAGE, SaleRegister, type SaleSettings } from './sale-register'

/** PDV-03: o pagamento sobre o LocalStore de verdade (Dexie no IndexedDB falso). Arroz 25,90 + café 18,99 = 44,89. */
const CASH: CatalogPaymentMethod = { id: 'cash', name: 'Dinheiro', kind: 'Cash', allowsChange: true }
const PIX: CatalogPaymentMethod = { id: 'pix', name: 'Pix', kind: 'Pix', allowsChange: false }
const CREDIT: CatalogPaymentMethod = { id: 'credit', name: 'Crédito', kind: 'CreditInstallments', allowsChange: false, cardBrands: ['Visa'], maxInstallments: 12 }

async function device() {
  const indexedDB = new IDBFactory()
  const locks = new InMemoryLockManager()
  const dbName = `pagamento-${Math.random()}`
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
  const settings: SaleSettings = { mode: 'keyboard', ...DEFAULT_SETTINGS }
  const open = async () => {
    const lock = await acquireWriterLock(writerLockName(dbName), locks)
    const store = await DexieLocalStore.open({ dbName, lock, indexedDB, IDBKeyRange, attempts: 1 })
    const register = await SaleRegister.open({ store, operatorId: 'op-1', salePrefix: '001-01-', settings: () => settings, cipher: aesDocumentCipher(async () => key) })
    return { store, register, lock }
  }

  const first = await open()
  const data = emptyCatalog()
  data.items.push({ id: 'arroz', name: 'Arroz', unit: 'UN', sku: null, fractional: false }, { id: 'cafe', name: 'Café', unit: 'UN', sku: null, fractional: false })
  data.barcodes.push({ id: 'b1', code: '7891000100103', itemId: 'arroz' }, { id: 'b2', code: '7896005800017', itemId: 'cafe' })
  data.prices.push({ id: 'p1', itemId: 'arroz', priceCents: 2_590, tableVersion: 1 }, { id: 'p2', itemId: 'cafe', priceCents: 1_899, tableVersion: 1 })
  data.paymentMethods.push(CASH, PIX, CREDIT)
  await first.store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, data, '2026-11-18T10:00:00Z')
  await first.register.addFromInput('7891000100103')
  await first.register.addFromInput('7896005800017')
  return { ...first, open }
}

const terminal = new ManualTerminal()
let n = 0
const charge = (method: CatalogPaymentMethod, amountCents: number, extra: { receivedCents?: number; cardBrand?: string; nsu?: string; note?: string } = {}) =>
  terminal.charge({ tenderId: `t-${++n}`, method, amountCents, operatorId: 'op-1', at: '2026-11-18T12:00:00Z', ...extra })

describe('pagamento da venda (PDV-03)', () => {
  it('três formas com centavos: InPayment, PartiallyPaid e Completed com número e a venda inteira na fila', async () => {
    const { store, register } = await device()
    const close = vi.spyOn(store, 'closeSale')
    await register.setCustomer({ document: '529.982.247-25' })
    expect(await register.startPayment()).toEqual({ ok: true })
    expect(register.current()?.inPayment).toBe(true)

    expect(await register.recordTender(await charge(PIX, 1_001))).toMatchObject({ kind: 'recorded', remainingCents: 3_488 })
    expect(await register.recordTender(await charge(CREDIT, 2_000, { cardBrand: 'Visa', nsu: '123456' }))).toMatchObject({ kind: 'recorded', remainingCents: 1_488 })
    const done = await register.recordTender(await charge(CASH, 1_488, { receivedCents: 2_000 }))
    expect(done).toMatchObject({ kind: 'completed', number: '001-01-000001', totalCents: 4_489, changeCents: 512 })
    expect(register.current()).toBeNull()
    expect(await store.saleJournal()).toHaveLength(0)
    expect((await store.pendingSummary()).count).toBe(1)

    const { entry } = await (close.mock.results[0].value as Promise<{ entry: OutboxEntry }>)
    const payload = entry.payload as { status: string; number: string; totalCents: number; changeCents: number; events: { type: string; data: Record<string, unknown> }[] }
    expect(payload).toMatchObject({ status: 'completed', number: '001-01-000001', totalCents: 4_489, changeCents: 512 })
    expect(payload.events.map((e) => e.type).filter((t) => t !== 'LineAdded')).toEqual(['SaleStarted', 'CustomerSet', 'PaymentStarted', 'TenderCaptured', 'TenderCaptured', 'TenderCaptured', 'SaleCompleted'])
    const tenders = payload.events.filter((e) => e.type === 'TenderCaptured').map((e) => e.data.tender as Tender)
    expect(tenders.map((t) => t.amountCents)).toEqual([1_001, 2_000, 1_488])
    expect(tenders.every((t) => t.confirmedBy === 'op-1' && t.integration === 'Manual')).toBe(true)
    expect(tenders[0]).toMatchObject({ needsReconciliation: true, operatorAttested: true })
    expect(tenders[2]).toMatchObject({ receivedCents: 2_000, changeCents: 512 })
    // Segurança: nenhum CPF em claro na fila; quem mexeu em dinheiro está em todo evento de pagamento.
    const json = JSON.stringify(entry)
    expect(json).not.toContain('52998224725')
    expect(json).not.toContain('529.982.247-25')
    expect(payload.events.filter((e) => ['PaymentStarted', 'SaleCompleted'].includes(e.type)).every((e) => e.data.operatorId === 'op-1')).toBe(true)
  })

  it('uma forma: dinheiro com troco fecha a venda', async () => {
    const { register } = await device()
    await register.startPayment()
    expect(await register.recordTender(await charge(CASH, 4_489, { receivedCents: 5_000 }))).toMatchObject({ kind: 'completed', changeCents: 511 })
  })

  it('invariante: o capturado não passa do que falta, troco só no dinheiro e nada de número de cartão', async () => {
    const { register } = await device()
    await register.startPayment()
    expect(await register.recordTender(await charge(PIX, 5_000))).toEqual({ kind: 'refused', message: 'O valor passa do que falta.' })
    const pixWithChange: Tender = { ...(await charge(PIX, 1_000)), changeCents: 100 }
    expect(await register.recordTender(pixWithChange)).toEqual({ kind: 'refused', message: 'Só o dinheiro dá troco.' })
    expect(await register.recordTender(await charge(CREDIT, 1_000, { cardBrand: 'Visa', nsu: '4111111111111111' }))).toEqual({ kind: 'refused', message: 'Não digite o número do cartão.' })
    expect(await register.recordTender(await charge(CREDIT, 1_000, { cardBrand: 'Visa', note: '4111 1111 1111 1111' }))).toEqual({ kind: 'refused', message: 'Não digite o número do cartão.' })
    expect(await register.recordTender(await charge(PIX, 0))).toMatchObject({ kind: 'refused' })
    expect(register.current()?.tenders).toHaveLength(0)
  })

  it('voltar aos itens só sem pagamento capturado (CA 7 e 8); no pagamento a venda não muda', async () => {
    const { register } = await device()
    await register.startPayment()
    expect(await register.addFromInput('7891000100103')).toEqual({ kind: 'locked', message: LOCKED_MESSAGE })
    expect(await register.park()).toBe(false)
    expect(await register.cancelPayment()).toEqual({ ok: true })
    expect(register.current()?.inPayment).toBe(false)
    expect(await register.addFromInput('7891000100103')).toMatchObject({ kind: 'added' })
    expect(saleTotal(register.current())).toBe(7_079)

    await register.startPayment()
    await register.recordTender(await charge(PIX, 1_000))
    expect(await register.cancelPayment()).toMatchObject({ ok: false })
    expect(register.current()?.inPayment).toBe(true)
    await expect(register.discard('Cliente desistiu')).rejects.toThrow('estorno')
  })

  it('a venda no pagamento volta como estava; a queda entre o último pagamento e o fechamento conclui ao reabrir', async () => {
    const first = await device()
    await first.register.startPayment()
    await first.register.recordTender(await charge(PIX, 1_001))
    first.lock.release()

    const second = await first.open()
    const sale = second.register.current()
    expect(sale?.inPayment).toBe(true)
    expect(sale?.tenders.map((t) => t.amountCents)).toEqual([1_001])

    // Simula a queda: o último pagamento gravado no diário, sem o fechamento.
    const last = tenderFrom({ tenderId: 'ultimo', method: CASH, amountCents: 3_488, receivedCents: 3_488, operatorId: 'op-1', at: '2026-11-18T12:01:00Z' }, 'Manual', 'Captured')
    await second.store.appendSaleEvents([{ id: 'e-ultimo', saleId: sale!.id, type: 'TenderCaptured', at: last.at, data: { tender: last } }])
    second.lock.release()

    const third = await first.open()
    expect(third.register.current()).toBeNull()
    expect(await third.store.saleJournal()).toHaveLength(0)
    expect((await third.store.pendingSummary()).count).toBe(1)
  })

  it('pagamento sem resposta (Unknown) fica gravado e, conferido como aprovado, fecha a venda (CA 11)', async () => {
    const { register } = await device()
    await register.startPayment()
    const unknown = { ...(await charge(CREDIT, 4_489, { cardBrand: 'Visa' })), status: 'Unknown' as const }
    expect(await register.recordTender(unknown)).toMatchObject({ kind: 'recorded', remainingCents: 4_489 })
    expect(register.current()?.tenders[0].status).toBe('Unknown')
    const approved = await terminal.check(unknown, true, '2026-11-18T12:02:00Z')
    expect(await register.recordTender(approved)).toMatchObject({ kind: 'completed' })
  })
})
