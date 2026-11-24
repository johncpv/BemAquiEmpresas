import type { LocalStore } from '@/pdv/local-store/local-store'
import { formatCents } from '@/pdv/money'
import { uuidv7 } from '@/pdv/uuid-v7'
import { approvalMessage, checkSecret, fromBase64, loginMessage, mac, type OfflineVerifier, toBase64 } from './offline-crypto'
import { advanceClock, CLOCK_META, type OfflineClock } from './offline-window'

/**
 * Entrada e aprovação sem rede no aparelho (OFF-03). Guarda no `LocalStore`:
 * - os verificadores (`offline.verifiers`), **cifrados** com a chave AES-GCM não extraível própria (`loadVerifierKey`); a lista inteira troca a cada
 *   sincronização, então quem saiu da loja, foi desativado ou trocou o PIN perde o verificador antigo (CA 4);
 * - os erros por pessoa (`offline.lockouts`): 5 seguidos bloqueiam a pessoa **neste aparelho** até a próxima sincronização, e o bloqueio sobe (CA 3);
 * - a entrada sem rede em curso (`offline.session`: quem, as ações e o id da entrada; nunca o PIN);
 * - o que a sessão de caixa já aprovou sem rede (`offline.approvals`), para os limites da loja (CA 13).
 * O segredo digitado só passa pela memória (WebCrypto) e nunca vai para o disco, a fila ou log.
 */
export const VERIFIERS_META = 'offline.verifiers'
export const LOCKOUTS_META = 'offline.lockouts'
export const SESSION_META = 'offline.session'
export const APPROVALS_META = 'offline.approvals'
export const CLOCK_REPORTED_META = 'offline.clockReported'
export const AUTH_OUTBOX_KIND = 'pos.auth'
export const APPROVAL_OUTBOX_KIND = 'pos.approval'
export const MAX_FAILURES = 5

export const NOT_RECEIVED_MESSAGE = 'Este caixa ainda não recebeu o seu cadastro. Conecte à internet uma vez.'
export const LOCKED_MESSAGE = 'Entrada bloqueada neste caixa até ele conectar à internet, por excesso de tentativas.'
export const ABOVE_LIMIT_MESSAGE = 'Acima do limite sem internet. Conecte para aprovar.'
export const PRICE_NEEDS_NETWORK = 'A troca de preço precisa de internet.'

/** Limites da loja para a aprovação sem rede (PP-22, recomendação aceita pelo PO em 24/11/2026), em centavos. */
export interface OfflineLimits {
  maxCents: number
  sessionCents: number
  sessionCount: number
  cancelSaleAllowed: boolean
}

export interface OfflineSession {
  userId: string
  name: string
  actions: string[]
  loginId: string
  at: string
}

interface Sealed { iv: string; data: string }
type Store = Pick<LocalStore, 'getMeta' | 'setMeta' | 'enqueue'>

export async function saveVerifiers(store: Pick<LocalStore, 'getMeta' | 'setMeta'>, verifiers: readonly OfflineVerifier[], serverAt: string, key: CryptoKey): Promise<void> {
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, new TextEncoder().encode(JSON.stringify(verifiers)))
  await store.setMeta(VERIFIERS_META, { iv: toBase64(iv.buffer), data: toBase64(data) } satisfies Sealed)
  await store.setMeta(LOCKOUTS_META, {}) // CA 3: o bloqueio sem rede dura até a próxima sincronização
  await recordServerTime(store, serverAt)
}

/** Os verificadores deste aparelho; lista vazia se nunca desceram ou se a chave sumiu (o aparelho pede rede). */
export async function loadVerifiers(store: Pick<LocalStore, 'getMeta'>, key: CryptoKey): Promise<OfflineVerifier[]> {
  const sealed = await store.getMeta<Sealed>(VERIFIERS_META)
  if (!sealed?.iv || !sealed.data) return []
  try {
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromBase64(sealed.iv) }, key, fromBase64(sealed.data))
    return JSON.parse(new TextDecoder().decode(plain)) as OfflineVerifier[]
  } catch {
    return []
  }
}

/** CA 14: guarda o horário do servidor como piso da janela (foto, delta ou resposta da subida). */
export async function recordServerTime(store: Pick<LocalStore, 'getMeta' | 'setMeta'>, serverAt: string, deviceNow = new Date()): Promise<OfflineClock> {
  const next = advanceClock(await store.getMeta<OfflineClock>(CLOCK_META), { serverAt, deviceNow })
  await store.setMeta(CLOCK_META, next)
  await store.setMeta(CLOCK_REPORTED_META, null)
  return next
}

/** Avança o maior horário visto (o "agora" não volta) e devolve o relógio guardado. */
export async function touchClock(store: Pick<LocalStore, 'getMeta' | 'setMeta'>, deviceNow = new Date()): Promise<OfflineClock | null> {
  const current = await store.getMeta<OfflineClock>(CLOCK_META)
  if (!current) return null
  const next = advanceClock(current, { deviceNow })
  if (next.highWater !== current.highWater) await store.setMeta(CLOCK_META, next)
  return next
}

/** CA 14: registra uma vez (até a próxima sincronização) o relógio do aparelho antes do piso; sobe na fila. */
export async function reportClockBehind(store: Store, floor: Date, deviceNow = new Date()): Promise<void> {
  if (await store.getMeta<boolean>(CLOCK_REPORTED_META)) return
  await store.setMeta(CLOCK_REPORTED_META, true)
  await store.enqueue(AUTH_OUTBOX_KIND, { type: 'ClockBehind', deviceAt: deviceNow.toISOString(), floorAt: floor.toISOString(), offline: true }, floor.toISOString())
}

const lockKey = (kind: string, userId: string) => `${kind}:${userId}`

export async function failuresOf(store: Pick<LocalStore, 'getMeta'>, kind: string, userId: string): Promise<number> {
  return (await store.getMeta<Record<string, number>>(LOCKOUTS_META))?.[lockKey(kind, userId)] ?? 0
}

export type SecretCheck<T> = { ok: true; value: T } | { ok: false; message: string; locked: boolean }

/**
 * Confere o segredo com o contador de erros deste aparelho (CA 3): 5 erros seguidos bloqueiam a pessoa até a próxima sincronização e o bloqueio
 * vai para a fila (auditoria no servidor). Certo: zera o contador e devolve a chave para a prova.
 */
async function checkWithLockout(store: Store, verifier: OfflineVerifier, secret: string, deviceId: string, wrongLabel: string, now: Date): Promise<SecretCheck<CryptoKey>> {
  const key = lockKey(verifier.kind, verifier.userId)
  const counts = (await store.getMeta<Record<string, number>>(LOCKOUTS_META)) ?? {}
  if ((counts[key] ?? 0) >= MAX_FAILURES) return { ok: false, message: LOCKED_MESSAGE, locked: true }
  const derived = /^\d{6}$/.test(secret) ? await checkSecret(secret, verifier, deviceId) : null
  if (derived) {
    if (counts[key]) await store.setMeta(LOCKOUTS_META, { ...counts, [key]: 0 })
    return { ok: true, value: derived }
  }

  const failures = (counts[key] ?? 0) + 1
  await store.setMeta(LOCKOUTS_META, { ...counts, [key]: failures })
  if (failures >= MAX_FAILURES) {
    await store.enqueue(AUTH_OUTBOX_KIND, { type: 'OfflineLocked', userId: verifier.userId, kind: verifier.kind, at: now.toISOString(), offline: true }, now.toISOString())
    return { ok: false, message: LOCKED_MESSAGE, locked: true }
  }

  const left = MAX_FAILURES - failures
  return { ok: false, message: `${wrongLabel} Restam ${left} ${left === 1 ? 'tentativa' : 'tentativas'}.`, locked: false }
}

/** CA 2: entrada sem rede. A prova do PIN vai na fila (o servidor confere com o PIN atual) e a sessão fica marcada como offline. */
export async function offlineLogin(store: Store, input: { verifier: OfflineVerifier | null; pin: string; deviceId: string; now?: Date }): Promise<SecretCheck<OfflineSession>> {
  const now = input.now ?? new Date()
  if (!input.verifier || input.verifier.kind !== 'operator') return { ok: false, message: NOT_RECEIVED_MESSAGE, locked: false }
  const checked = await checkWithLockout(store, input.verifier, input.pin, input.deviceId, 'PIN incorreto.', now)
  if (!checked.ok) return checked
  const at = now.toISOString()
  const loginId = uuidv7(now.getTime())
  const proof = await mac(checked.value, loginMessage(input.deviceId, input.verifier.userId, loginId, at))
  await store.enqueue(AUTH_OUTBOX_KIND, { type: 'OfflineLogin', userId: input.verifier.userId, loginId, at, proof, offline: true }, at)
  const session: OfflineSession = { userId: input.verifier.userId, name: input.verifier.displayName, actions: [...input.verifier.actions], loginId, at }
  await store.setMeta(SESSION_META, session)
  return { ok: true, value: session }
}

export const loadOfflineSession = async (store: Pick<LocalStore, 'getMeta'>) => (await store.getMeta<OfflineSession | null>(SESSION_META)) ?? null
export const endOfflineSession = (store: Pick<LocalStore, 'setMeta'>) => store.setMeta(SESSION_META, null)

export type OfflineAction = 'Discount' | 'ChangePrice' | 'CancelItem' | 'CancelSale'

/** CA 13: os limites da loja (os mesmos que o servidor reconfere). Null: cabe. */
export function offlineLimitProblem(limits: OfflineLimits | null | undefined, action: OfflineAction, impactCents: number, used: { count: number; cents: number }): string | null {
  if (action === 'ChangePrice') return PRICE_NEEDS_NETWORK
  if (!limits || limits.maxCents <= 0 || limits.sessionCount <= 0) return ABOVE_LIMIT_MESSAGE
  if (action === 'CancelSale' && !limits.cancelSaleAllowed) return ABOVE_LIMIT_MESSAGE
  if (impactCents > limits.maxCents || used.count + 1 > limits.sessionCount || used.cents + impactCents > limits.sessionCents) return ABOVE_LIMIT_MESSAGE
  return null
}

/** O que falta para o limite, para a ajuda do diálogo ("até R$ 50,00 por aprovação"). */
export const limitsText = (limits: OfflineLimits) =>
  `Sem internet, cada aprovação vai até ${formatCents(limits.maxCents)}, com até ${limits.sessionCount} aprovações e ${formatCents(limits.sessionCents)} por turno.`

export interface OfflineApprovalInput {
  verifier: OfflineVerifier | null
  code: string
  deviceId: string
  operatorId: string
  cashSessionId: string | null
  limits: OfflineLimits | null | undefined
  action: OfflineAction
  reason: string
  saleId: string
  lineId: string | null
  beforeCents: number
  afterCents: number
  now?: Date
}

/**
 * CA 7, 13 e 15: aprovação do supervisor sem rede com o **código sem internet** (PP-39), conferida no aparelho, dentro dos limites da loja. Vira o
 * evento `pos.approval` com a prova; na subida o servidor reconfere tudo e grava a aprovação com `online = false`. Supervisor igual ao operador: recusa.
 */
export async function offlineApprove(store: Store, input: OfflineApprovalInput): Promise<SecretCheck<{ approvalId: string; supervisorId: string; supervisorName: string }>> {
  const now = input.now ?? new Date()
  const verifier = input.verifier
  if (!verifier || verifier.kind !== 'supervisor') return { ok: false, message: NOT_RECEIVED_MESSAGE, locked: false }
  if (verifier.userId === input.operatorId) return { ok: false, message: 'Outra pessoa precisa aprovar.', locked: false }
  if (!input.cashSessionId) return { ok: false, message: 'Abra o caixa antes.', locked: false }
  const saved = await store.getMeta<{ sessionId: string; count: number; cents: number }>(APPROVALS_META)
  const used = saved?.sessionId === input.cashSessionId ? saved : { sessionId: input.cashSessionId, count: 0, cents: 0 }
  const impact = Math.abs(input.beforeCents - input.afterCents)
  const limit = offlineLimitProblem(input.limits, input.action, impact, used)
  if (limit) return { ok: false, message: limit, locked: false }
  const checked = await checkWithLockout(store, verifier, input.code, input.deviceId, 'Código incorreto.', now)
  if (!checked.ok) return checked
  const at = now.toISOString()
  const approvalId = uuidv7(now.getTime())
  const proof = await mac(checked.value, approvalMessage({
    deviceId: input.deviceId, approvalId, action: input.action, operatorId: input.operatorId, supervisorId: verifier.userId, saleId: input.saleId, lineId: input.lineId,
    beforeCents: input.beforeCents, afterCents: input.afterCents, at,
  }))
  await store.enqueue(APPROVAL_OUTBOX_KIND, {
    approvalId, action: input.action, operatorId: input.operatorId, supervisorId: verifier.userId, reason: input.reason, saleId: input.saleId, lineId: input.lineId,
    beforeCents: input.beforeCents, afterCents: input.afterCents, at, cashSessionId: input.cashSessionId, proof, offline: true,
  }, at)
  await store.setMeta(APPROVALS_META, { sessionId: input.cashSessionId, count: used.count + 1, cents: used.cents + impact })
  return { ok: true, value: { approvalId, supervisorId: verifier.userId, supervisorName: verifier.displayName } }
}

/** Recusa da conferência sem rede: a mensagem vai direto para a tela. */
export class OfflineAccessError extends Error {}

/** Janela da loja em horas (PP-10): o parâmetro em horas da loja; sem ele, os dias da foto. */
export function windowHoursOf(store: Pick<LocalStore, 'catalogState'>): number | null {
  const state = store.catalogState()
  return state?.storeSettings?.offlineWindowHours ?? (state?.offlineWindowDays ? state.offlineWindowDays * 24 : null)
}
