import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { eventHash, uploadPayload } from './event-chain'
import { sealPending, type UploadBody, uploadOutbox, type UploadResponse } from './event-upload'

/** OFF-02: a cadeia de hash (mesmo vetor do servidor), o selo da fila de saída, o reenvio a partir do não confirmado e o CPF fora dos eventos. */
async function openStore() {
  const locks = new InMemoryLockManager()
  const dbName = 'pdv-sync-teste'
  const lock = await acquireWriterLock(writerLockName(dbName), locks)
  return DexieLocalStore.open({ dbName, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
}

const DEVICE = { deviceId: '019a0000-0000-7000-8000-0000000000d1', registerId: '019a0000-0000-7000-8000-0000000000c1', deviceEpoch: 2 }

const accept = (body: UploadBody): UploadResponse => ({
  results: body.events.map((e) => ({ eventId: e.eventId, deviceEpoch: e.deviceEpoch, seq: e.seq, outcome: 'Accepted', replayed: false })),
  acks: [{ deviceEpoch: 2, seq: body.events.at(-1)?.seq ?? 0 }],
})

describe('cadeia de hash dos eventos', () => {
  it('dá o mesmo hash do servidor (PosEventChain.Hash)', async () => {
    const payload = '{"saleId":"019a0000-0000-7000-8000-000000000001","número":"ação"}'
    const base = { eventId: '019a0000-0000-7000-8000-0000000000e1', ...DEVICE, seq: 7, schemaVersion: 1, kind: 'pos.sale', occurredAt: '2026-11-19T12:00:00.000Z', payload }
    expect(await eventHash({ ...base, prevHash: null })).toBe('4ec63d582faec531deef5e592e5e192a54d94d782b67145461ca46103215f207')
    expect(await eventHash({ ...base, prevHash: 'ab'.repeat(32) })).toBe('7234ba05a9aafa5d72938a7ee3bafe8231f8cd159d0864e5fcd59f407209aaa5')
  })

  it('tira o nome do cliente do evento que sobe; o documento nunca esteve nele', () => {
    const sale = { saleId: 's1', events: [{ type: 'CustomerSet', data: { customer: { customerId: 'c1', customerName: 'Maria Silva', documentId: 'd1', documentKind: 'cpf' } } }] }
    expect(JSON.stringify(uploadPayload('pos.sale', sale))).not.toContain('Maria')
    expect(JSON.stringify(uploadPayload('pos.sale', sale))).toContain('"documentId":"d1"')
  })
})

describe('fila de saída (OFF-02)', () => {
  it('sela em ordem com seq contínuo e hash encadeado, e o selo não muda no reenvio', async () => {
    const store = await openStore()
    await store.enqueue('pos.cash', { type: 'CashSessionOpened' }, '2026-11-19T12:00:00.000Z')
    await store.enqueue('pos.sale', { saleId: 's1', status: 'completed', events: [] }, '2026-11-19T12:01:00.000Z')
    expect(await sealPending(store, DEVICE)).toBe(2)
    const [a, b] = await store.outbox()
    expect([a.seal?.seq, b.seal?.seq]).toEqual([1, 2])
    expect(a.seal?.prevHash).toBeNull()
    expect(b.seal?.prevHash).toBe(a.seal?.hash)
    expect(b.seal?.hash).toBe(await eventHash({ eventId: b.id, kind: b.kind, ...b.seal!, prevHash: b.seal!.prevHash }))

    // Evento novo continua a cadeia; os já selados não mudam.
    await store.enqueue('pos.print', { type: 'ReceiptPrinted' }, '2026-11-19T12:02:00.000Z')
    expect(await sealPending(store, DEVICE)).toBe(1)
    const rows = await store.outbox()
    expect(rows[0].seal).toEqual(a.seal)
    expect(rows[2].seal?.seq).toBe(3)
    expect(rows[2].seal?.prevHash).toBe(b.seal?.hash)
  })

  it('a rede cai no meio: o confirmado sai da fila e o resto sobe de novo a partir dele, com o mesmo selo', async () => {
    const store = await openStore()
    for (let i = 0; i < 5; i++) await store.enqueue('pos.cash', { n: i }, `2026-11-19T12:0${i}:00.000Z`)
    const sent: UploadBody[] = []
    // Primeira rodada: o servidor só confirma os 2 primeiros (a resposta do resto se perdeu).
    const partial = await uploadOutbox({
      store, device: DEVICE, batchSize: 5,
      send: async (body) => { sent.push(body); return { ...accept(body), results: accept(body).results.slice(0, 2) } },
    })
    expect(partial).toMatchObject({ sent: 2, remaining: 3 })
    expect(sent[0].oldestPendingAt).toBe('2026-11-19T12:00:00.000Z')

    const second = await uploadOutbox({ store, device: DEVICE, send: async (body) => { sent.push(body); return accept(body) } })
    expect(second).toMatchObject({ sent: 3, remaining: 0 })
    expect(sent[1].events.map((e) => e.seq)).toEqual([3, 4, 5])
    expect(sent[1].events[0]).toEqual(sent[0].events[2])
  })

  it('rede fora: o erro sobe e nada sai da fila', async () => {
    const store = await openStore()
    await store.enqueue('pos.cash', { n: 1 })
    await expect(uploadOutbox({ store, device: DEVICE, send: async () => { throw new Error('sem rede') } })).rejects.toThrow('sem rede')
    expect((await store.pendingSummary()).count).toBe(1)
  })

  it('o CPF da venda vai no bloco à parte, nunca no evento', async () => {
    const store = await openStore()
    const cpf = '52998224725'
    await store.enqueue('pos.sale', {
      saleId: 's1', status: 'completed', events: [{ type: 'CustomerSet', data: { customer: { customerId: null, customerName: null, documentId: 'd1', documentKind: 'cpf' } } }],
    })
    let body: UploadBody | null = null
    await uploadOutbox({
      store, device: DEVICE, document: async (id) => (id === 'd1' ? { kind: 'cpf', value: cpf } : null),
      send: async (b) => { body = b; return accept(b) },
    })
    expect(body!.documents).toEqual([{ saleId: 's1', kind: 'cpf', value: cpf }])
    expect(JSON.stringify(body!.events)).not.toContain(cpf)
  })

  it('época nova do caixa recomeça o seq em 1', async () => {
    const store = await openStore()
    await store.enqueue('pos.cash', { n: 1 })
    await uploadOutbox({ store, device: DEVICE, send: async (b) => accept(b) })
    await store.enqueue('pos.cash', { n: 2 })
    let body: UploadBody | null = null
    await uploadOutbox({ store, device: { ...DEVICE, deviceEpoch: 3 }, send: async (b) => { body = b; return accept(b) } })
    expect(body!.events[0]).toMatchObject({ deviceEpoch: 3, seq: 1, prevHash: null })
  })
})
