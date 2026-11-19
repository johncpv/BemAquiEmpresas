import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import type { PosHandshake } from '@/api/generated/model'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { PdvSale } from './PdvSale'

/** PDV-03 na tela: dinheiro + cartão na mesma venda, troco em destaque, cartão sem número e as formas que precisam de rede. */
async function openStore(methods = true) {
  const lock = await acquireWriterLock(writerLockName('pagar'), new InMemoryLockManager())
  const store = await DexieLocalStore.open({ dbName: `pagar-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
  const data = emptyCatalog()
  data.items.push({ id: 'cafe', name: 'Café Pilão 500g', unit: 'UN', sku: null, fractional: false })
  data.barcodes.push({ id: 'b1', code: '7896005800017', itemId: 'cafe' })
  data.prices.push({ id: 'p1', itemId: 'cafe', priceCents: 1_899, tableVersion: 1 })
  if (methods) {
    data.paymentMethods.push(
      { id: 'cash', name: 'Dinheiro', kind: 'Cash', allowsChange: true },
      { id: 'credit', name: 'Crédito', kind: 'Credit', allowsChange: false, cardBrands: ['Visa', 'Mastercard'], maxInstallments: 1 },
      { id: 'fiado', name: 'Fiado', kind: 'OnAccount', allowsChange: false },
    )
  }
  await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, data, '2026-11-18T10:00:00Z')
  await store.setMeta('cash.session', { openedAt: '2026-11-18T10:00:00Z', operatorId: 'op' })
  return store
}

const handshake = {
  deviceId: 'd1', storeId: 'loja', storeCode: '001', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Keyboard', salePrefix: '001-01-', serverTime: '2026-11-18T10:00:00Z',
} satisfies PosHandshake

async function saleWithCoffee(online: boolean, methods = true) {
  const store = await openStore(methods)
  render(<PdvSale store={store} handshake={handshake} online={online} operator={{ id: 'op', name: 'Diego', canCancelItem: false }} searchCustomers={null} onLeave={() => {}} />)
  const code = await screen.findByLabelText('Código, quantidade (3*código) ou nome')
  fireEvent.change(code, { target: { value: '7896005800017' } })
  fireEvent.keyDown(code, { key: 'Enter' })
  await screen.findByText('Café Pilão 500g')
  fireEvent.click(screen.getByRole('button', { name: /F10/ }))
  return store
}

describe('tela de pagamento (PDV-03)', () => {
  it('cartão e dinheiro na mesma venda: bandeira, NSU sem número de cartão, troco e venda concluída', async () => {
    const store = await saleWithCoffee(true)
    expect(await screen.findByLabelText('Falta: R$ 18,99')).toBeTruthy()
    // CA 9: fiado com rede fica oculto enquanto a PDV-08 não chega.
    expect(screen.queryByRole('button', { name: /Fiado/ })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Crédito/ }))
    const value = screen.getByLabelText('Valor')
    // CA 3: fora do dinheiro, o valor é limitado ao que falta.
    fireEvent.change(value, { target: { value: '50,00' } })
    expect((await screen.findByRole('alert')).textContent).toBe('Só o dinheiro dá troco.')
    expect((value as HTMLInputElement).value).toBe('18,99')

    fireEvent.change(value, { target: { value: '10,00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cartão aprovado' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Escolha a bandeira do cartão.')
    fireEvent.click(screen.getByRole('button', { name: 'Visa' }))
    fireEvent.change(screen.getByLabelText('NSU (opcional)'), { target: { value: '4111111111111111' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cartão aprovado' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Não digite o número do cartão.')
    fireEvent.change(screen.getByLabelText('NSU (opcional)'), { target: { value: '123456' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cartão aprovado' }))
    expect(await screen.findByLabelText('Falta: R$ 8,99')).toBeTruthy()
    // CA 7: com pagamento capturado, não volta aos itens.
    expect((screen.getByRole('button', { name: 'Voltar aos itens' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: /Dinheiro/ }))
    fireEvent.change(screen.getByLabelText('Valor recebido'), { target: { value: '20,00' } })
    expect(screen.getByText('R$ 11,01')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar recebimento' }))

    expect(await screen.findByText('Venda concluída', { selector: 'h2' })).toBeTruthy()
    expect(screen.getByLabelText('Troco: R$ 11,01')).toBeTruthy()
    expect(screen.getByText(/001-01-000001/)).toBeTruthy()
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Nova venda' })))
    expect((await store.pendingSummary()).count).toBe(1)

    fireEvent.click(screen.getByRole('button', { name: 'Nova venda' }))
    expect(await screen.findByText('Bipe ou busque um produto para começar.')).toBeTruthy()
  })

  it('sem rede, o fiado aparece desabilitado; voltar aos itens sem pagamento devolve a venda', async () => {
    await saleWithCoffee(false)
    const fiado = await screen.findByRole('button', { name: /Fiado/ })
    expect((fiado as HTMLButtonElement).disabled).toBe(true)
    expect(fiado.textContent).toContain('Disponível com internet.')
    fireEvent.click(screen.getByRole('button', { name: 'Voltar aos itens' }))
    expect(await screen.findByLabelText('Total a pagar: R$ 18,99')).toBeTruthy()
  })

  it('sem forma de pagamento na loja, explica o caminho para cadastrar', async () => {
    await saleWithCoffee(true, false)
    expect(await screen.findByText('Nenhuma forma de pagamento ativa nesta loja.')).toBeTruthy()
  })
})
