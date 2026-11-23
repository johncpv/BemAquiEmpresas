import { fireEvent, render, screen, within } from '@testing-library/react'
import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import type { PosHandshake } from '@/api/generated/model'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import { PdvSale } from '@/pages/pdv/sale/PdvSale'

/** CX-02 na tela: a contagem não mostra esperado, diferença nem total de vendas (CA 2 e 3); depois, só "enviado para conferência" (CA 6 e 11). */
async function countingStore() {
  const lock = await acquireWriterLock(writerLockName('fechar'), new InMemoryLockManager())
  const store = await DexieLocalStore.open({ dbName: `fechar-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
  const data = emptyCatalog()
  data.paymentMethods.push({ id: 'cash', name: 'Dinheiro', kind: 'Cash', allowsChange: true }, { id: 'debito', name: 'Débito', kind: 'Debit', allowsChange: false })
  await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, data, '2026-11-18T10:00:00Z')
  // Um aparelho antigo que ainda tivesse o dinheiro calculado guardado: a tela de contagem não usa.
  await store.setMeta('cash.session', {
    id: 's1', registerId: 'r1', deviceEpoch: 1, openedAt: '2026-11-18T11:02:00Z', operatorId: 'op', operatorName: 'Diego', openingCents: 15_000, salesCount: 3,
    cashSalesCents: 48_765, movements: [], counting: true,
  })
  return store
}

const handshake = {
  deviceId: 'd1', storeId: 'loja', storeCode: '001', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Keyboard', salePrefix: '001-01-', serverTime: '2026-11-18T10:00:00Z',
} satisfies PosHandshake

describe('fechar caixa com conferência cega (CX-02)', () => {
  it('conta sem ver o esperado, confirma uma vez e só diz que foi para conferência', async () => {
    const store = await countingStore()
    render(<PdvSale store={store} handshake={handshake} online operator={{ id: 'op', name: 'Diego', canCancelItem: false, canCashOut: true, canCashIn: true }} searchCustomers={null} onLeave={() => {}} />)
    const screenCount = await screen.findByRole('region', { name: 'Fechar caixa' })
    expect(screenCount.textContent).not.toMatch(/esperado|diferença|total de vendas/i)
    expect(screenCount.textContent).not.toContain('487,65')
    expect(screenCount.textContent).not.toContain('637,65')

    const pad = within(screenCount).getByRole('group', { name: 'Teclado numérico' })
    for (const d of ['2', '0', '0', '0', '0']) fireEvent.click(within(pad).getByRole('button', { name: d }))
    fireEvent.click(within(screenCount).getByRole('button', { name: /Débito/ }))
    for (const d of ['5', '0', '0', '0']) fireEvent.click(within(pad).getByRole('button', { name: d }))
    fireEvent.click(within(screenCount).getByRole('button', { name: 'Continuar' }))

    const kept = screen.getByRole('region', { name: 'Fundo que fica' })
    expect(kept.textContent).toMatch(/Dinheiro contado: R\$\s200,00/)
    expect(kept.textContent).toMatch(/Recolhimento ao cofre: R\$\s50,00/)
    fireEvent.click(within(kept).getByRole('button', { name: 'Confirmar contagem' }))
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Confirmar contagem' }))

    expect(await screen.findByText('Fechamento enviado para conferência.', { selector: '[role="status"]' })).toBeTruthy()
    const receipt = screen.getByRole('article', { name: 'Comprovante do fechamento' })
    expect(receipt.textContent).toContain('RECOLHIMENTO AO COFRE')
    expect(receipt.textContent).not.toMatch(/esperado|diferença/i)
    const counted = (await store.outbox()).map((e) => e.payload as { type: string; lines?: unknown }).find((p) => p.type === 'CashCounted')
    expect(counted?.lines).toEqual([{ methodId: 'cash', countedCents: 20_000, denominations: null }, { methodId: 'debito', countedCents: 5_000, denominations: null }])

    fireEvent.click(screen.getByRole('button', { name: 'Concluir' }))
    expect(await screen.findByText('Abra o caixa para começar a vender.')).toBeTruthy()
  })
})
