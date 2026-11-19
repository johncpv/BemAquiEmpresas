import { IDBFactory, IDBKeyRange } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { emptyCatalog } from '@/pdv/local-store/catalog-types'
import { DexieLocalStore } from '@/pdv/local-store/dexie-local-store'
import { acquireWriterLock, InMemoryLockManager, writerLockName } from '@/pdv/local-store/writer-lock'
import {
  cashAlert, cashBlocked, cashKeptCents, checkMovement, closeCashSession, drawerCents, lastOpeningCents, loadCashSession, openCashSession, openedByOtherMessage,
  recordMovement, recordSaleInSession,
} from './cash-session'

/** CX-01 no aparelho: abertura, sangria e suprimento gravados com o evento na fila; alerta e teto (CA 7 e 8); a validação nunca revela o saldo (CA 4). */
async function openStore() {
  const lock = await acquireWriterLock(writerLockName('caixa'), new InMemoryLockManager())
  const store = await DexieLocalStore.open({ dbName: `caixa-${Math.random()}`, lock, indexedDB: new IDBFactory(), IDBKeyRange, attempts: 1 })
  await store.replaceCatalog({ schemaVersion: 1, cursor: '1', storeId: 'loja', storeName: 'Loja' }, emptyCatalog(), '2026-11-18T10:00:00Z')
  return store
}

const ana = { id: 'ana', name: 'Ana' }
const at = (hhmm: string) => new Date(`2026-11-18T${hhmm}:00-03:00`)

describe('sessão de caixa no aparelho (CX-01)', () => {
  it('abre com o fundo de troco, grava o evento junto e guarda o fundo como padrão da próxima (CA 1)', async () => {
    const store = await openStore()
    expect(await loadCashSession(store)).toBeNull()
    const session = await openCashSession(store, { operator: ana, openingCents: 15_000, registerId: 'r1', deviceEpoch: 2 }, at('08:02'))
    expect(session.operatorId).toBe('ana')
    expect(session.movements).toHaveLength(1)
    expect((await loadCashSession(store))?.id).toBe(session.id)
    expect(await lastOpeningCents(store)).toBe(15_000)
    expect((await store.pendingSummary()).count).toBe(1)
    await expect(openCashSession(store, { operator: ana, openingCents: 0, registerId: 'r1', deviceEpoch: 2 })).rejects.toThrow('O caixa já está aberto.')
    expect((await store.pendingSummary()).count).toBe(1)
  })

  it('outro operador vê quem abriu e desde quando (CA 2); o marcador provisório da PDV-02 não vale como turno', async () => {
    const store = await openStore()
    await store.setMeta('cash.session', { openedAt: '2026-11-18T10:00:00Z', operatorId: 'ana' })
    expect(await loadCashSession(store)).toBeNull()
    const session = await openCashSession(store, { operator: ana, openingCents: 0, registerId: 'r1', deviceEpoch: 1 }, at('08:02'))
    expect(openedByOtherMessage(session, 'rui')).toBe('Este caixa já está aberto por Ana desde 08:02.')
    expect(openedByOtherMessage(session, 'ana')).toBeNull()
  })

  it('sangria e suprimento exigem valor e motivo, e nunca recusam por passar do dinheiro calculado (CA 4 e 5)', async () => {
    expect(checkMovement(0, 'Banco')).toEqual({ ok: false, field: 'amount', message: 'Informe um valor maior que zero.' })
    expect(checkMovement(null, 'Banco').ok).toBe(false)
    expect(checkMovement(1_000, '   ')).toEqual({ ok: false, field: 'reason', message: 'Informe o motivo.' })
    expect(checkMovement(1_000, 'x'.repeat(201)).ok).toBe(false)
    expect(checkMovement(1_000, '  Depósito  ')).toEqual({ ok: true, amountCents: 1_000, reason: 'Depósito' })

    const store = await openStore()
    await openCashSession(store, { operator: ana, openingCents: 10_000, registerId: 'r1', deviceEpoch: 1 })
    // Acima do calculado (R$ 100,00): aceita, marcada, sem mensagem nenhuma sobre o saldo.
    const drop = await recordMovement(store, { kind: 'CashOut', amountCents: 50_000, reason: 'Depósito no banco', operator: ana })
    expect(drop.movement.aboveExpected).toBe(true)
    const supply = await recordMovement(store, { kind: 'CashIn', amountCents: 5_000, reason: 'Troco', operator: ana })
    expect(supply.movement.aboveExpected).toBe(false)
    expect(supply.session.movements.map((m) => m.kind)).toEqual(['Opening', 'CashOut', 'CashIn'])
    expect(supply.session.movements.every((m) => m.operatorId === 'ana')).toBe(true)
    expect((await store.pendingSummary()).count).toBe(3)
    await expect(recordMovement(store, { kind: 'CashOut', amountCents: 100, reason: '', operator: ana })).rejects.toThrow('Informe o motivo.')
    expect((await store.pendingSummary()).count).toBe(3)
  })

  it('conta a venda e o dinheiro que fica na gaveta; avisa acima do limite (CA 7) e bloqueia o Dinheiro acima do teto (CA 8)', async () => {
    const store = await openStore()
    await openCashSession(store, { operator: ana, openingCents: 10_000, registerId: 'r1', deviceEpoch: 1 })
    const tenders = [
      { status: 'Captured' as const, methodType: 'Cash', amountCents: 90_000 },
      { status: 'Captured' as const, methodType: 'Credit', amountCents: 50_000 },
      { status: 'Failed' as const, methodType: 'Cash', amountCents: 70_000 },
    ]
    expect(cashKeptCents(tenders)).toBe(90_000)
    const session = (await recordSaleInSession(store, tenders))!
    expect(session.salesCount).toBe(1)
    expect(drawerCents(session)).toBe(100_000)

    const limits = { alertCents: 100_000, ceilingCents: 150_000 }
    expect(cashAlert(session, limits)).toBeNull()
    const more = (await recordSaleInSession(store, [{ status: 'Captured', methodType: 'Cash', amountCents: 40_000 }]))!
    expect(cashAlert(more, limits)).toBe('Dinheiro em caixa acima de R$ 1.000,00. Faça uma sangria.')
    expect(cashBlocked(more, limits)).toBeNull()
    expect(cashAlert(more, { alertCents: null, ceilingCents: null })).toBeNull()

    const full = (await recordSaleInSession(store, [{ status: 'Captured', methodType: 'Cash', amountCents: 10_001 }]))!
    expect(cashBlocked(full, limits)).toBe('Dinheiro em caixa acima de R$ 1.500,00. Faça uma sangria para receber em dinheiro.')
    const after = await recordMovement(store, { kind: 'CashOut', amountCents: 100_000, reason: 'Cofre', operator: ana })
    expect(cashBlocked(after.session, limits)).toBeNull()
    expect(cashAlert(after.session, limits)).toBeNull()
  })

  it('fechar encerra o turno com o evento; sem turno, a venda não conta', async () => {
    const store = await openStore()
    await openCashSession(store, { operator: ana, openingCents: 0, registerId: 'r1', deviceEpoch: 1 })
    await closeCashSession(store, ana)
    expect(await loadCashSession(store)).toBeNull()
    expect((await store.pendingSummary()).count).toBe(2)
    expect(await recordSaleInSession(store, [{ status: 'Captured', methodType: 'Cash', amountCents: 100 }])).toBeNull()
    await closeCashSession(store, ana)
    expect((await store.pendingSummary()).count).toBe(2)
  })
})
