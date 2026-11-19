import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { emptyCatalog, type CatalogData } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { aesDocumentCipher } from './customer-document'
import { formatQuantity, parseQuantity } from './quantity'
import { saleTotal } from './sale-model'
import { DEFAULT_SETTINGS, SALE_OUTBOX_KIND, SaleRegister, type SaleSettings } from './sale-register'

/** PDV-02: a venda do caixa sobre o LocalStore de verdade (Dexie no IndexedDB falso). */
function catalog(extra = 0): CatalogData {
  const data = emptyCatalog()
  const add = (id: string, name: string, priceCents: number, code: string, fractional = false, packQuantity?: number) => {
    data.items.push({ id, name, unit: fractional ? 'KG' : 'UN', sku: null, fractional })
    data.barcodes.push({ id: `bc-${code}`, code, itemId: id, packQuantity })
    data.prices.push({ id: `pr-${id}`, itemId: id, priceCents, tableVersion: 3 })
  }
  add('arroz', 'Arroz Camil 5kg', 2_590, '7891000100103')
  data.barcodes.push({ id: 'bc-cx', code: '17891000100100', itemId: 'arroz', packQuantity: 12 })
  add('queijo', 'Queijo minas', 4_990, '2000003', true)
  add('cafe', 'Café Pilão 500g', 1_899, '7896005800017')
  for (let i = 0; i < extra; i++) add(`item-${i}`, `Produto ${i} granel`, 100 + i, String(7_800_000_000_000 + i))
  return data
}

async function device(extra = 0) {
  const indexedDB = new IDBFactory()
  const locks = new InMemoryLockManager()
  const dbName = `venda-${Math.random()}`
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
  const settings: SaleSettings = { mode: 'keyboard', ...DEFAULT_SETTINGS }
  const open = async () => {
    const lock = await acquireWriterLock(writerLockName(dbName), locks)
    const store = await DexieLocalStore.open({ dbName, lock, indexedDB, IDBKeyRange, attempts: 1 })
    const register = await SaleRegister.open({ store, operatorId: 'op-1', salePrefix: '001-01-', settings: () => settings, cipher: aesDocumentCipher(async () => key) })
    return { store, register, lock }
  }

  const first = await open()
  await first.store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, catalog(extra), '2026-11-18T10:00:00Z')
  return { ...first, settings, open }
}

describe('lançar item (CA 1 a 6)', () => {
  it('bipa: entra com nome, quantidade 1 e o preço da tabela; o mesmo item de novo soma na linha', async () => {
    const { register } = await device()
    const first = await register.addFromInput('7891000100103')
    expect(first).toMatchObject({ kind: 'added', merged: false, line: { name: 'Arroz Camil 5kg', quantity: '1', unitCents: 2_590, priceTableVersion: 3 } })
    expect(await register.addFromInput('7891000100103')).toMatchObject({ kind: 'added', merged: true })
    expect(register.current()?.lines).toHaveLength(1)
    expect(register.current()?.lines[0].quantity).toBe('2')
    expect(saleTotal(register.current())).toBe(5_180)
  })

  it('linhas separadas quando o caixa pede (CA 5)', async () => {
    const { register, settings } = await device()
    settings.mergeSameItem = false
    await register.addFromInput('7891000100103')
    await register.addFromInput('7891000100103')
    expect(register.current()?.lines).toHaveLength(2)
  })

  it('código da embalagem entra com a quantidade dela; 3*código define a quantidade (CA 2 e 6)', async () => {
    const { register } = await device()
    expect(await register.addFromInput('17891000100100')).toMatchObject({ kind: 'added', line: { quantity: '12' } })
    expect(await register.addFromInput('3*7896005800017')).toMatchObject({ kind: 'added', line: { quantity: '3', unitCents: 1_899 } })
    expect(await register.addFromInput('2,5*7896005800017')).toMatchObject({ kind: 'invalid-quantity' })
  })

  it('código que não existe: a venda não muda (CA 3); texto vira busca', async () => {
    const { register, store } = await device()
    expect(await register.addFromInput('7899999999999')).toEqual({ kind: 'not-found', code: '7899999999999' })
    expect(register.current()).toBeNull()
    expect(await store.saleJournal()).toHaveLength(0)
    expect(await register.addFromInput('arroz')).toEqual({ kind: 'search', text: 'arroz' })
  })

  it('pesável sem quantidade pede a quantidade; aceita 3 casas e recusa fração no item por unidade (CA 9)', async () => {
    const { register, store } = await device()
    expect(await register.addFromInput('2000003')).toMatchObject({ kind: 'needs-quantity' })
    const queijo = store.get('items', 'queijo')!
    expect(await register.addItem(queijo, '0,375')).toMatchObject({ kind: 'added', line: { quantity: '0.375' } })
    expect(saleTotal(register.current())).toBe(1_871) // 49,90 × 0,375 = 18,7125
    const arroz = await register.addFromInput('7891000100103')
    const lineId = arroz.kind === 'added' ? arroz.line.lineId : ''
    expect(await register.setQuantity(lineId, '1,5')).toEqual({ ok: false, message: 'Este produto é vendido por unidade: use um número inteiro.' })
    expect(await register.setQuantity(lineId, '4')).toEqual({ ok: true })
    expect(saleTotal(register.current())).toBe(1_871 + 4 * 2_590)
    expect(parseQuantity('1,2345', true).ok).toBe(false)
    expect(formatQuantity('0.375', true)).toBe('0,375')
  })
})

describe('editar a venda (CA 10 a 13)', () => {
  it('o último item sai sem supervisor; outra linha pede pos.cancel_item; tudo registrado', async () => {
    const { register, store } = await device()
    await register.addFromInput('7891000100103')
    const cafe = await register.addFromInput('7896005800017')
    const arrozId = register.current()!.lines[0].lineId
    expect(await register.removeLine(arrozId, false)).toEqual({ kind: 'needs-authorization' })
    expect(await register.removeLine(cafe.kind === 'added' ? cafe.line.lineId : '', false)).toEqual({ kind: 'removed' })
    expect(await register.removeLine(arrozId, true)).toEqual({ kind: 'removed' })
    expect(register.current()?.lines).toHaveLength(0)
    const removed = (await store.saleJournal()).filter((e) => e.type === 'LineRemoved').map((e) => e.data)
    expect(removed).toEqual([
      expect.objectContaining({ reason: 'last-item', operatorId: 'op-1' }),
      expect.objectContaining({ reason: 'authorized', operatorId: 'op-1' }),
    ])
  })

  it('o último lançamento que somou na linha sai só ele; a loja pode desligar a remoção livre', async () => {
    const { register, settings } = await device()
    await register.addFromInput('7891000100103')
    await register.addFromInput('2*7891000100103')
    const lineId = register.current()!.lines[0].lineId
    await register.removeLine(lineId, false)
    expect(register.current()!.lines[0].quantity).toBe('1')
    await register.addFromInput('7891000100103')
    settings.freeRemoveLast = false
    expect(await register.removeLine(lineId, false)).toEqual({ kind: 'needs-authorization' })
  })

  it('diminuir a quantidade segue a regra da remoção: linha antiga pede pos.cancel_item, o último lançamento passa (achado 3)', async () => {
    const { register, store } = await device()
    await register.addFromInput('3*7891000100103')
    await register.addFromInput('2*7896005800017')
    const [arroz, cafe] = register.current()!.lines
    expect(await register.setQuantity(arroz.lineId, '1')).toMatchObject({ ok: false, needsAuthorization: true })
    expect(register.current()!.lines[0].quantity).toBe('3')
    expect(await register.setQuantity(arroz.lineId, '5')).toEqual({ ok: true })
    expect(await register.setQuantity(cafe.lineId, '1')).toEqual({ ok: true })
    expect(await register.setQuantity(arroz.lineId, '2', true)).toEqual({ ok: true })
    expect(register.current()!.lines.map((l) => l.quantity)).toEqual(['2', '1'])
    const changed = (await store.saleJournal()).filter((e) => e.type === 'LineChanged').map((e) => e.data)
    expect(changed).toHaveLength(3)
    expect(changed.every((d) => (d as { operatorId?: string }).operatorId === 'op-1')).toBe(true)
  })

  it('o último lançamento que somou na linha só desce até o que havia antes dele', async () => {
    const { register } = await device()
    await register.addFromInput('2*7891000100103')
    await register.addFromInput('7891000100103')
    const lineId = register.current()!.lines[0].lineId
    expect(await register.setQuantity(lineId, '1')).toMatchObject({ ok: false, needsAuthorization: true })
    expect(await register.setQuantity(lineId, '2')).toEqual({ ok: true })
  })

  it('observação fica gravada na linha (CA 11)', async () => {
    const { register, open, lock } = await device()
    await register.addFromInput('7896005800017')
    await register.setNote(register.current()!.lines[0].lineId, '  sem açúcar ')
    lock.release()
    const again = await open()
    expect(again.register.current()!.lines[0].note).toBe('sem açúcar')
  })

  it('espera e retomada (CA 12)', async () => {
    const { register } = await device()
    await register.addFromInput('7891000100103')
    const firstId = register.current()!.id
    expect(await register.park()).toBe(true)
    expect(register.current()).toBeNull()
    await register.addFromInput('7896005800017')
    await register.resume(firstId)
    expect(register.current()!.id).toBe(firstId)
    expect(register.parked().map((s) => s.lines[0].name)).toEqual(['Café Pilão 500g'])
  })

  it('descarte com motivo: número na hora, os eventos saem do diário e sobem numa entrada só (CA 13)', async () => {
    const { register, store } = await device()
    await register.addFromInput('7891000100103')
    await register.setCustomer({ document: '529.982.247-25' })
    await expect(register.discard('  ')).rejects.toThrow('motivo')
    expect(await register.discard('Cliente desistiu')).toEqual({ number: '001-01-000001' })
    expect(register.current()).toBeNull()
    expect(await store.saleJournal()).toHaveLength(0)
    const pending = await store.pendingSummary()
    expect(pending.count).toBe(1)
    await register.addFromInput('7896005800017')
    expect(await register.discard('Teste')).toEqual({ number: '001-01-000002' })
    expect(SALE_OUTBOX_KIND).toBe('pos.sale')
  })
})

describe('cliente (CA 14)', () => {
  it('CPF validado, cifrado num registro à parte; o evento leva só o id', async () => {
    const { register, store } = await device()
    await register.addFromInput('7891000100103')
    expect(await register.setCustomer({ document: '111.111.111-11' })).toEqual({ ok: false, message: 'CPF inválido: confira os números.' })
    expect(await register.setCustomer({ document: '529.982.247-25' })).toEqual({ ok: true })
    const sale = register.current()!
    expect(sale.customer).toMatchObject({ documentKind: 'cpf' })
    const journal = JSON.stringify(await store.saleJournal())
    expect(journal).not.toContain('52998224725')
    const record = await store.getCustomerDocument(sale.customer!.documentId!)
    expect(JSON.stringify(record)).not.toContain('52998224725')
    expect(await register.customerDocument(sale)).toBe('52998224725')
    // tirar o documento apaga o registro
    await register.setCustomer({ document: null })
    expect(await store.getCustomerDocument(sale.customer!.documentId!)).toBeNull()
    expect(register.current()!.customer).toBeNull()
  })
})

describe('queda e desempenho (CA 1, 8 e 18)', () => {
  it('reabrir o caixa traz a venda e as vendas em espera como estavam', async () => {
    const { register, open, lock } = await device()
    await register.addFromInput('7891000100103')
    await register.park()
    await register.addFromInput('3*7896005800017')
    lock.release()
    const again = await open()
    expect(again.register.current()!.lines[0]).toMatchObject({ name: 'Café Pilão 500g', quantity: '3' })
    expect(again.register.parked()).toHaveLength(1)
  })

  it('com 20 mil itens: bipar e gravar em menos de 100 ms; busca de 3 letras em menos de 100 ms', async () => {
    const { register, store } = await device(20_000)
    const scans: number[] = []
    for (let i = 0; i < 20; i++) {
      const started = performance.now()
      const result = await register.addFromInput(String(7_800_000_000_000 + i * 997))
      scans.push(performance.now() - started)
      expect(result.kind).toBe('added')
    }

    const searches: number[] = []
    for (const term of ['gra', 'pro', 'caf', 'arr', '19 gr']) {
      const started = performance.now()
      store.searchItems(term, 30)
      searches.push(performance.now() - started)
    }

    const p95 = (values: number[]) => values.sort((a, b) => a - b)[Math.ceil(values.length * 0.95) - 1]
    expect(p95(scans)).toBeLessThan(100)
    expect(p95(searches)).toBeLessThan(100)
  }, 60_000)
})
