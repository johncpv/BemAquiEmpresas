import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import type { PosHandshake } from '@/api/generated/model'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { PdvSale } from './PdvSale'

/** PDV-02 na tela: caixa fechado (CA 17), leitura no campo de código (CA 1 e 3) e o leitor com o foco em outro campo (CA 4). */
async function openStore() {
  const lock = await acquireWriterLock(writerLockName('tela'), new InMemoryLockManager())
  const store = await DexieLocalStore.open({ dbName: `tela-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
  const data = emptyCatalog()
  data.items.push({ id: 'cafe', name: 'Café Pilão 500g', unit: 'UN', sku: null, fractional: false })
  data.barcodes.push({ id: 'b1', code: '7896005800017', itemId: 'cafe' })
  data.prices.push({ id: 'p1', itemId: 'cafe', priceCents: 1_899, tableVersion: 1 })
  await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, data, '2026-11-18T10:00:00Z')
  return store
}

const handshake = {
  deviceId: 'd1', storeId: 'loja', storeCode: '001', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Keyboard', salePrefix: '001-01-', serverTime: '2026-11-18T10:00:00Z',
} satisfies PosHandshake

function fastType(target: Element, text: string) {
  for (const key of text) fireEvent.keyDown(target, { key })
  fireEvent.keyDown(target, { key: 'Enter' })
}

describe('tela de venda (PDV-02)', () => {
  it('sem caixa aberto pede para abrir; aberto, lança pelo código e avisa o código que não existe', async () => {
    const store = await openStore()
    render(<PdvSale store={store} handshake={handshake} online operator={{ id: 'op', name: 'Diego', canCancelItem: false }} searchCustomers={null} onLeave={() => {}} />)
    expect(await screen.findByText('Abra o caixa para começar a vender.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Abrir caixa' }))
    expect(await screen.findByText('Bipe ou busque um produto para começar.')).toBeTruthy()

    const code = screen.getByLabelText('Código, quantidade (3*código) ou nome')
    fireEvent.change(code, { target: { value: '3*7896005800017' } })
    fireEvent.keyDown(code, { key: 'Enter' })
    expect(await screen.findByText('Café Pilão 500g')).toBeTruthy()
    expect(screen.getByLabelText('Total a pagar: R$ 56,97')).toBeTruthy()

    fireEvent.change(code, { target: { value: '7899999999999' } })
    fireEvent.keyDown(code, { key: 'Enter' })
    expect((await screen.findByRole('alert')).textContent).toBe('Produto não encontrado: 7899999999999')
  })

  it('leitura com o foco em outro campo lança o item e não suja o campo', async () => {
    const store = await openStore()
    await store.setMeta('cash.session', { openedAt: '2026-11-18T10:00:00Z', operatorId: 'op' })
    render(
      <>
        <input aria-label="Outro campo" defaultValue="abc" />
        <PdvSale store={store} handshake={handshake} online operator={{ id: 'op', name: 'Diego', canCancelItem: false }} searchCustomers={null} onLeave={() => {}} />
      </>,
    )
    await screen.findByText('Bipe ou busque um produto para começar.')
    const other = screen.getByLabelText<HTMLInputElement>('Outro campo')
    fastType(other, '7896005800017')
    expect(await screen.findByText('Café Pilão 500g')).toBeTruthy()
    expect(other.value).toBe('abc')
    await waitFor(() => expect(screen.getByLabelText('Total a pagar: R$ 18,99')).toBeTruthy())
  })
})
