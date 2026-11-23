import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { DEFAULT_SETTINGS, SaleRegister } from '@/pdv/sale/sale-register'
import { localStockGuard } from './stock-guard'
import { syncStock } from './stock-sync'

describe('caixa sem vender item sem saldo (PDV-05, PP-02)', () => {
  it('recusa acima do último saldo recebido e libera com a opção ligada', async () => {
    const dbName = `saldo-${Math.random()}`
    const lock = await acquireWriterLock(writerLockName(dbName), new InMemoryLockManager())
    const store = await DexieLocalStore.open({ dbName, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
    const data = emptyCatalog()
    data.items.push({ id: 'arroz', name: 'Arroz', unit: 'UN', sku: null, fractional: false })
    data.prices.push({ id: 'p', itemId: 'arroz', priceCents: 2_590, tableVersion: 1 })
    await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja' }, data, '2026-11-23T10:00:00Z')
    const register = await SaleRegister.open({ store, operatorId: 'op', salePrefix: '001-01-', settings: () => ({ mode: 'keyboard', ...DEFAULT_SETTINGS }), stock: localStockGuard(store) })

    await syncStock(store, async () => ({ sellWithoutStock: false, full: true, cursor: 1, levels: [{ productId: 'arroz', available: 2 }] }))
    expect(await register.addItem(data.items[0], '2')).toMatchObject({ kind: 'added' })
    // A venda já tem as 2 do saldo: a terceira passa do disponível (a mensagem diz o saldo da loja).
    expect(await register.addItem(data.items[0], '1')).toMatchObject({ kind: 'no-stock', message: 'Arroz: só 2 UN em saldo nesta loja. A empresa não vende item sem saldo.' })

    await syncStock(store, async (since) => {
      expect(since).toBe(1)
      return { sellWithoutStock: true, full: false, cursor: 2, levels: [] }
    })
    expect(await register.addItem(data.items[0], '1')).toMatchObject({ kind: 'added' })
  })
})
