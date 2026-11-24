import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { OfflineMenu } from '@/pages/pdv/PdvOfflineEntry'
import {
  ABOVE_LIMIT_MESSAGE, failuresOf, LOCKED_MESSAGE, loadVerifiers, NOT_RECEIVED_MESSAGE, offlineApprove, offlineLimitProblem, offlineLogin, PRICE_NEEDS_NETWORK, saveVerifiers,
  VERIFIERS_META,
} from './offline-access'
import { approvalMessage, deriveKey, loginMessage, mac, type OfflineVerifier, toBase64, verifierMessage } from './offline-crypto'
import { NEEDS_NETWORK, OFFLINE_DISABLED } from './offline-features'

const DEVICE = '019a0000-0000-7000-8000-0000000000d1'
const OPERATOR = '019a0000-0000-7000-8000-0000000000aa'
const SUPERVISOR = '019a0000-0000-7000-8000-0000000000bb'

/** O que o servidor manda: PBKDF2 do segredo (poucas iterações no teste) e o HMAC da mensagem do verificador. O segredo nunca vai junto. */
async function verifierFor(userId: string, kind: 'operator' | 'supervisor', secret: string, actions: string[] = []): Promise<OfflineVerifier> {
  const params = { algorithm: 'PBKDF2-SHA512', iterations: 1000, salt: toBase64(crypto.getRandomValues(new Uint8Array(16)).buffer), keyLength: 32 }
  const nonce = toBase64(crypto.getRandomValues(new Uint8Array(16)).buffer)
  const verifier = await mac(await deriveKey(secret, params), verifierMessage(kind, DEVICE, userId, nonce))
  return { userId, displayName: kind === 'operator' ? 'Olga S.' : 'Sara S.', kind, ...params, nonce, verifier, actions }
}

function memoryStore() {
  const meta = new Map<string, unknown>()
  const queue: { kind: string; payload: Record<string, unknown>; at?: string }[] = []
  return {
    queue,
    meta,
    getMeta: async <T,>(key: string) => meta.get(key) as T | undefined,
    setMeta: async (key: string, value: unknown) => { meta.set(key, value) },
    enqueue: async (kind: string, payload: unknown, at?: string) => {
      queue.push({ kind, payload: payload as Record<string, unknown>, at })
      return { seq: queue.length, id: String(queue.length), kind, createdAt: at ?? '', payload }
    },
  }
}

const limits = { maxCents: 5000, sessionCents: 15000, sessionCount: 5, cancelSaleAllowed: true }

describe('entrada e aprovação sem rede no aparelho (OFF-03)', () => {
  it('guarda os verificadores cifrados com a chave própria e lê de volta (CA 1)', async () => {
    const store = memoryStore()
    const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
    const list = [await verifierFor(OPERATOR, 'operator', '482915', ['create'])]
    await saveVerifiers(store, list, '2026-11-24T12:00:00.000Z', key)
    expect(JSON.stringify(store.meta.get(VERIFIERS_META))).not.toContain('Olga')
    expect(await loadVerifiers(store, key)).toEqual(list)
    const other = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
    expect(await loadVerifiers(store, other)).toEqual([])
  })

  it('PIN certo entra marcado como offline com a prova na fila; quem não chegou ao caixa vê o aviso (CA 2 e 5)', async () => {
    const store = memoryStore()
    const verifier = await verifierFor(OPERATOR, 'operator', '482915', ['create', 'cash_out'])
    expect(await offlineLogin(store, { verifier: null, pin: '482915', deviceId: DEVICE })).toEqual({ ok: false, message: NOT_RECEIVED_MESSAGE, locked: false })
    const result = await offlineLogin(store, { verifier, pin: '482915', deviceId: DEVICE, now: new Date('2026-11-24T12:00:00.000Z') })
    expect(result.ok).toBe(true)
    const event = store.queue.at(-1)!
    expect(event.kind).toBe('pos.auth')
    expect(event.payload).toMatchObject({ type: 'OfflineLogin', userId: OPERATOR, at: '2026-11-24T12:00:00.000Z', offline: true })
    expect(JSON.stringify(event.payload)).not.toContain('482915')
    const expected = await mac(await deriveKey('482915', verifier), loginMessage(DEVICE, OPERATOR, event.payload.loginId as string, '2026-11-24T12:00:00.000Z'))
    expect(event.payload.proof).toBe(expected)
  })

  it('cinco PINs errados bloqueiam a pessoa neste aparelho até sincronizar e o bloqueio vai para a fila (CA 3)', async () => {
    const store = memoryStore()
    const verifier = await verifierFor(OPERATOR, 'operator', '482915')
    for (let i = 1; i <= 4; i++) {
      const wrong = await offlineLogin(store, { verifier, pin: '000001', deviceId: DEVICE })
      expect(wrong).toMatchObject({ ok: false, locked: false })
      if (!wrong.ok) expect(wrong.message).toContain(`Restam ${5 - i}`)
    }

    expect(await offlineLogin(store, { verifier, pin: '000001', deviceId: DEVICE })).toEqual({ ok: false, message: LOCKED_MESSAGE, locked: true })
    expect(store.queue.at(-1)!.payload).toMatchObject({ type: 'OfflineLocked', userId: OPERATOR })
    expect(await offlineLogin(store, { verifier, pin: '482915', deviceId: DEVICE })).toMatchObject({ ok: false, locked: true })
    expect(await failuresOf(store, 'operator', OPERATOR)).toBe(5)
    // A próxima sincronização zera os bloqueios (a lista nova desce).
    await saveVerifiers(store, [verifier], '2026-11-24T13:00:00.000Z', await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']))
    expect(await failuresOf(store, 'operator', OPERATOR)).toBe(0)
  })

  it('supervisor aprova com o código sem internet dentro dos limites; acima, outra pessoa e troca de preço são recusados (CA 7, 13 e 15)', async () => {
    const store = memoryStore()
    const supervisor = await verifierFor(SUPERVISOR, 'supervisor', '295173', ['cancel_item', 'discount'])
    const base = {
      verifier: supervisor, code: '295173', deviceId: DEVICE, operatorId: OPERATOR, cashSessionId: 'sessao', limits, action: 'CancelItem' as const, reason: 'Cliente desistiu',
      saleId: '019a0000-0000-7000-8000-0000000000c1', lineId: '019a0000-0000-7000-8000-0000000000c2', beforeCents: 2000, afterCents: 0, now: new Date('2026-11-24T12:00:00.000Z'),
    }
    const ok = await offlineApprove(store, base)
    expect(ok.ok).toBe(true)
    const event = store.queue.at(-1)!
    expect(event.kind).toBe('pos.approval')
    expect(event.payload).toMatchObject({ action: 'CancelItem', supervisorId: SUPERVISOR, operatorId: OPERATOR, beforeCents: 2000, afterCents: 0, cashSessionId: 'sessao', offline: true })
    expect(event.payload.proof).toBe(await mac(await deriveKey('295173', supervisor), approvalMessage({
      deviceId: DEVICE, approvalId: event.payload.approvalId as string, action: 'CancelItem', operatorId: OPERATOR, supervisorId: SUPERVISOR, saleId: base.saleId, lineId: base.lineId,
      beforeCents: 2000, afterCents: 0, cashSessionId: 'sessao', at: '2026-11-24T12:00:00.000Z', reason: 'Cliente desistiu',
    })))

    expect(await offlineApprove(store, { ...base, beforeCents: 6000 })).toEqual({ ok: false, message: ABOVE_LIMIT_MESSAGE, locked: false })
    expect(await offlineApprove(store, { ...base, operatorId: SUPERVISOR })).toMatchObject({ ok: false, message: 'Outra pessoa precisa aprovar.' })
    expect(await offlineApprove(store, { ...base, action: 'ChangePrice' })).toMatchObject({ ok: false, message: PRICE_NEEDS_NETWORK })
    expect(await offlineApprove(store, { ...base, code: '111111' })).toMatchObject({ ok: false, locked: false })
  })

  it('limites por turno: quantidade, soma e cancelamento de venda desligado', () => {
    expect(offlineLimitProblem(limits, 'Discount', 5000, { count: 0, cents: 0 })).toBeNull()
    expect(offlineLimitProblem(limits, 'Discount', 1000, { count: 5, cents: 1000 })).toBe(ABOVE_LIMIT_MESSAGE)
    expect(offlineLimitProblem(limits, 'Discount', 1000, { count: 1, cents: 14500 })).toBe(ABOVE_LIMIT_MESSAGE)
    expect(offlineLimitProblem({ ...limits, cancelSaleAllowed: false }, 'CancelSale', 100, { count: 0, cents: 0 })).toBe(ABOVE_LIMIT_MESSAGE)
    expect(offlineLimitProblem(null, 'Discount', 100, { count: 0, cents: 0 })).toBe(ABOVE_LIMIT_MESSAGE)
  })

  it('o menu sem rede mostra desabilitado o que precisa de internet, cada um com o motivo (CA 11)', () => {
    render(<OfflineMenu />)
    const list = screen.getByRole('list', { name: 'Disponível só com internet' })
    expect(list.querySelectorAll('button:disabled')).toHaveLength(OFFLINE_DISABLED.length)
    expect(screen.getAllByText(NEEDS_NETWORK)).toHaveLength(7)
    expect(screen.getByText('Fiado')).toBeTruthy()
  })
})
