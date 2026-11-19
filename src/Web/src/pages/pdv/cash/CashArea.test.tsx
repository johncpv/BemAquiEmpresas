import { fireEvent, render, screen, within } from '@testing-library/react'
import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import type { PosHandshake } from '@/api/generated/model'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { PdvSale } from '@/pages/pdv/sale/PdvSale'

/** CX-01 na tela: painel sem o esperado (CA 10), permissão (CA 6), sangria com comprovante (CA 4), outro operador (CA 2) e teto (CA 8). */
async function openStore(cashSalesCents = 0) {
  const lock = await acquireWriterLock(writerLockName('painel'), new InMemoryLockManager())
  const store = await DexieLocalStore.open({ dbName: `painel-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
  const data = emptyCatalog()
  data.items.push({ id: 'cafe', name: 'Café Pilão 500g', unit: 'UN', sku: null, fractional: false })
  data.barcodes.push({ id: 'b1', code: '7896005800017', itemId: 'cafe' })
  data.prices.push({ id: 'p1', itemId: 'cafe', priceCents: 1_899, tableVersion: 1 })
  data.paymentMethods.push({ id: 'cash', name: 'Dinheiro', kind: 'Cash', allowsChange: true }, { id: 'pix', name: 'Pix', kind: 'Pix', allowsChange: false })
  await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja', cashLimits: { alertCents: 100_000, ceilingCents: 200_000 } }, data, '2026-11-18T10:00:00Z')
  await store.setMeta('cash.session', {
    id: 's1', registerId: 'r1', deviceEpoch: 1, openedAt: '2026-11-18T11:02:00Z', operatorId: 'op', operatorName: 'Diego', openingCents: 15_000, salesCount: 3,
    cashSalesCents, movements: [],
  })
  return store
}

const handshake = {
  deviceId: 'd1', storeId: 'loja', storeCode: '001', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Keyboard', salePrefix: '001-01-', serverTime: '2026-11-18T10:00:00Z',
} satisfies PosHandshake

const operator = (canCashOut: boolean, id = 'op', name = 'Diego') => ({ id, name, canCancelItem: false, canCashOut, canCashIn: true })

describe('caixa: painel, sangria e suprimento (CX-01)', () => {
  it('o painel mostra operador, abertura e vendas sem o total esperado; sangria sem permissão é barrada', async () => {
    const store = await openStore()
    render(<PdvSale store={store} handshake={handshake} online operator={operator(false)} searchCustomers={null} onLeave={() => {}} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Caixa' }))
    const panel = screen.getByRole('region', { name: 'Caixa' })
    expect(within(panel).getByText('Diego')).toBeTruthy()
    expect(within(panel).getByText('3')).toBeTruthy()
    expect(within(panel).getByText('Nenhuma movimentação neste turno.')).toBeTruthy()
    expect(panel.textContent).not.toMatch(/esperado|150,00/i)

    fireEvent.click(within(panel).getByRole('button', { name: 'Sangria' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Você não tem permissão para esta ação.')
  })

  it('sangria com valor e motivo sai no comprovante em duas vias com assinatura e entra no painel', async () => {
    const store = await openStore()
    render(<PdvSale store={store} handshake={handshake} online operator={operator(true)} searchCustomers={null} onLeave={() => {}} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Caixa' }))
    fireEvent.click(screen.getByRole('button', { name: 'Sangria' }))
    const sheet = await screen.findByRole('dialog', { name: 'Sangria' })
    fireEvent.click(within(sheet).getByRole('button', { name: 'Registrar sangria' }))
    expect(within(sheet).getByRole('alert').textContent).toBe('Informe um valor maior que zero.')
    for (const d of ['5', '0', '0', '0', '0']) fireEvent.click(within(sheet).getByRole('button', { name: d }))
    fireEvent.click(within(sheet).getByRole('button', { name: 'Registrar sangria' }))
    expect(within(sheet).getByRole('alert').textContent).toBe('Informe o motivo.')
    fireEvent.change(within(sheet).getByLabelText('Motivo'), { target: { value: 'Depósito no banco' } })
    fireEvent.click(within(sheet).getByRole('button', { name: 'Registrar sangria' }))

    const receipt = await screen.findByRole('dialog', { name: 'Sangria registrada' })
    expect(within(receipt).getAllByText('Assinatura de quem retira', { exact: false })).toHaveLength(2)
    expect(within(receipt).getAllByText(/Depósito no banco/)).toHaveLength(2)
    fireEvent.click(within(receipt).getByRole('button', { name: 'Concluir' }))
    expect(await screen.findByText(/Sangria · Depósito no banco/)).toBeTruthy()
    expect((await store.pendingSummary()).count).toBe(1)
  })

  it('outro operador não vende no turno de quem abriu (CA 2)', async () => {
    const store = await openStore()
    render(<PdvSale store={store} handshake={handshake} online operator={operator(true, 'rui', 'Rui')} searchCustomers={null} onLeave={() => {}} />)
    const opened = new Date('2026-11-18T11:02:00Z').toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
    expect(await screen.findByText(`Este caixa já está aberto por Diego desde ${opened}.`)).toBeTruthy()
    expect(screen.queryByLabelText('Código, quantidade (3*código) ou nome')).toBeNull()
  })

  it('acima do teto da loja, a forma Dinheiro fica bloqueada e o Pix continua (CA 8)', async () => {
    const store = await openStore(190_000)
    render(<PdvSale store={store} handshake={handshake} online operator={operator(true)} searchCustomers={null} onLeave={() => {}} />)
    const code = await screen.findByLabelText('Código, quantidade (3*código) ou nome')
    fireEvent.change(code, { target: { value: '7896005800017' } })
    fireEvent.keyDown(code, { key: 'Enter' })
    await screen.findByText('Café Pilão 500g')
    fireEvent.click(screen.getByRole('button', { name: /Pagar/ }))
    expect(await screen.findByText(/Dinheiro em caixa acima de R\$\s2\.000,00\. Faça uma sangria para receber em dinheiro\./)).toBeTruthy()
    expect((within(screen.getByRole('group', { name: 'Escolha a forma' })).getByRole('button', { name: /Dinheiro/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((within(screen.getByRole('group', { name: 'Escolha a forma' })).getByRole('button', { name: /Pix/ }) as HTMLButtonElement).disabled).toBe(false)
  })
})
