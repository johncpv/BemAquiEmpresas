import type { CashLimits } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { type Cents, formatCents, sumCents } from '@/pdv/money'
import type { Tender } from '@/pdv/payment/payment-model'
import { uuidv7 } from '@/pdv/uuid-v7'

/**
 * Sessão de caixa (turno) no aparelho (CX-01). **Local-first**: abrir, sangrar, suprir e fechar gravam aqui e põem o evento na fila de saída numa
 * transação só (`LocalStore.updateMeta`); a subida é da OFF-02, que chama a gravação no servidor (`CashSessionIngestion`).
 * - Uma sessão por caixa: neste aparelho, quem chega com o caixa aberto por outro operador vê "Este caixa já está aberto por Fulano desde 08:02.".
 * - Toda ação com dinheiro leva o `operatorId`.
 * - O **dinheiro calculado** (fundo + dinheiro das vendas + suprimentos − sangrias) fica guardado só para o alerta e o teto, que são limites fixos da
 *   loja: nunca aparece na tela nem recusa uma sangria (conferência cega, CX-02). Sangria acima dele é aceita e vai marcada (`aboveExpected`).
 */
export const CASH_SESSION_META = 'cash.session'
/** O fundo de troco da última abertura, padrão da próxima (CA 1). */
export const LAST_OPENING_META = 'cash.lastOpening'

/** Tipo da entrada da fila de saída; o evento vai em `payload.type` (`CashSessionOpened`, `CashOutRecorded`, `CashInRecorded`, `CashSessionClosed`). */
export const CASH_OUTBOX_KIND = 'pos.cash'

export const MAX_REASON_LENGTH = 200
/** O mesmo teto do servidor (`numeric(18,2)` com folga): R$ 9.999.999,99. */
export const MAX_AMOUNT_CENTS = 999_999_999

export type CashMovementKind = 'Opening' | 'CashOut' | 'CashIn'

export interface LocalCashMovement {
  id: string
  kind: CashMovementKind
  amountCents: Cents
  reason: string | null
  operatorId: string
  operatorName: string
  at: string
  aboveExpected: boolean
}

export interface LocalCashSession {
  id: string
  registerId: string | null
  deviceEpoch: number | null
  openedAt: string
  operatorId: string
  operatorName: string
  openingCents: Cents
  /** Vendas concluídas na sessão (painel, CA 10). */
  salesCount: number
  /** Dinheiro que ficou na gaveta pelas vendas (recebido − troco). **Interno**: nunca vai para a tela. */
  cashSalesCents: Cents
  movements: LocalCashMovement[]
  /** CX-02: "Fechar caixa" acionado; o turno espera a contagem (não volta a vender). */
  counting?: boolean
  /** CX-02 CA 10: quantas vezes a sessão foi reaberta na retaguarda (a retomada neste aparelho). */
  reopenCount?: number
}

export interface CashOperator {
  id: string
  name: string
}

export type { CashLimits }

/** Sessão válida guardada no aparelho; o marcador provisório da PDV-02 (sem id) não vale como turno. */
export async function loadCashSession(store: Pick<LocalStore, 'getMeta'>): Promise<LocalCashSession | null> {
  const saved = await store.getMeta<Partial<LocalCashSession>>(CASH_SESSION_META)
  return saved && typeof saved.id === 'string' && typeof saved.operatorId === 'string' ? (saved as LocalCashSession) : null
}

export async function lastOpeningCents(store: Pick<LocalStore, 'getMeta'>): Promise<Cents | null> {
  const v = await store.getMeta<number>(LAST_OPENING_META)
  return typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 ? v : null
}

/** "Este caixa já está aberto por Ana desde 08:02." (CA 2), quando a sessão é de outro operador. */
export function openedByOtherMessage(session: LocalCashSession, operatorId: string): string | null {
  if (session.operatorId === operatorId) return null
  return `Este caixa já está aberto por ${session.operatorName} desde ${clock(session.openedAt)}.`
}

export const clock = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

/** Dinheiro calculado na gaveta (só para o alerta e o teto). */
export function drawerCents(session: LocalCashSession): Cents {
  const moves = session.movements.filter((m) => m.kind !== 'Opening').map((m) => (m.kind === 'CashOut' ? -m.amountCents : m.amountCents))
  return sumCents([session.openingCents, session.cashSalesCents, ...moves])
}

/** Dinheiro que fica na gaveta por uma venda: o valor dos pagamentos em dinheiro capturados (o troco já saiu do `amountCents`). */
export const cashKeptCents = (tenders: readonly Pick<Tender, 'status' | 'methodType' | 'amountCents'>[]): Cents =>
  sumCents(tenders.filter((t) => t.status === 'Captured' && t.methodType === 'Cash').map((t) => t.amountCents))

/** CA 7: aviso de sangria quando o dinheiro passa do limite de alerta da loja. O texto mostra o limite, nunca o saldo. */
export function cashAlert(session: LocalCashSession | null, limits: CashLimits | null): string | null {
  if (!session || limits?.alertCents == null) return null
  return drawerCents(session) > limits.alertCents ? `Dinheiro em caixa acima de ${formatCents(limits.alertCents)}. Faça uma sangria.` : null
}

/** CA 8: com o dinheiro acima do teto, a forma Dinheiro fica bloqueada até a sangria (as outras continuam). */
export function cashBlocked(session: LocalCashSession | null, limits: CashLimits | null): string | null {
  if (!session || limits?.ceilingCents == null) return null
  return drawerCents(session) > limits.ceilingCents ? `Dinheiro em caixa acima de ${formatCents(limits.ceilingCents)}. Faça uma sangria para receber em dinheiro.` : null
}

export type CashCheck = { ok: true; amountCents: Cents; reason: string } | { ok: false; field: 'amount' | 'reason'; message: string }

/**
 * Valida a sangria ou o suprimento (CA 4 e 5): valor maior que zero e motivo de 1 a 200 caracteres. **Não** compara com o dinheiro calculado: a
 * recusa revelaria o saldo.
 */
export function checkMovement(amountCents: Cents | null, reason: string): CashCheck {
  if (amountCents === null || !Number.isSafeInteger(amountCents) || amountCents <= 0) return { ok: false, field: 'amount', message: 'Informe um valor maior que zero.' }
  if (amountCents > MAX_AMOUNT_CENTS) return { ok: false, field: 'amount', message: 'Valor acima do permitido.' }
  const text = reason.trim()
  if (!text) return { ok: false, field: 'reason', message: 'Informe o motivo.' }
  if (text.length > MAX_REASON_LENGTH) return { ok: false, field: 'reason', message: `O motivo tem até ${MAX_REASON_LENGTH} caracteres.` }
  return { ok: true, amountCents, reason: text }
}

/** Abre a sessão (CA 1): grava o turno e o evento `CashSessionOpened` (com o fundo de troco) juntos, e guarda o fundo como padrão da próxima. */
export async function openCashSession(store: LocalStore, input: { operator: CashOperator; openingCents: Cents; registerId: string | null; deviceEpoch: number | null }, now = new Date()): Promise<LocalCashSession> {
  if (!Number.isSafeInteger(input.openingCents) || input.openingCents < 0 || input.openingCents > MAX_AMOUNT_CENTS) throw new Error('Fundo de troco inválido.')
  const at = now.toISOString()
  const id = uuidv7(now.getTime())
  const opening: LocalCashMovement = {
    id: uuidv7(now.getTime()), kind: 'Opening', amountCents: input.openingCents, reason: null, operatorId: input.operator.id, operatorName: input.operator.name, at, aboveExpected: false,
  }
  const session: LocalCashSession = {
    id, registerId: input.registerId, deviceEpoch: input.deviceEpoch, openedAt: at, operatorId: input.operator.id, operatorName: input.operator.name,
    openingCents: input.openingCents, salesCount: 0, cashSalesCents: 0, movements: [opening],
  }
  await store.updateMeta(CASH_SESSION_META, (current) => {
    if (current && typeof (current as Partial<LocalCashSession>).id === 'string') throw new CashSessionError('O caixa já está aberto.')
    return session
  }, {
    kind: CASH_OUTBOX_KIND, at,
    payload: {
      type: 'CashSessionOpened', sessionId: id, registerId: input.registerId, deviceEpoch: input.deviceEpoch, operatorId: input.operator.id,
      openingCents: input.openingCents, openingMovementId: opening.id, at,
    },
  })
  await store.setMeta(LAST_OPENING_META, input.openingCents)
  return session
}

export class CashSessionError extends Error {}

/**
 * Sangria ou suprimento (CA 4 e 5): o movimento e o evento (`CashOutRecorded`/`CashInRecorded`) na mesma transação. A sangria acima do dinheiro
 * calculado é aceita e vai marcada (`aboveExpected`), sem nada na tela.
 */
export async function recordMovement(store: LocalStore, input: { kind: 'CashOut' | 'CashIn'; amountCents: Cents; reason: string; operator: CashOperator }, now = new Date()): Promise<{ session: LocalCashSession; movement: LocalCashMovement }> {
  const check = checkMovement(input.amountCents, input.reason)
  if (!check.ok) throw new CashSessionError(check.message)
  const at = now.toISOString()
  let result: { session: LocalCashSession; movement: LocalCashMovement } | null = null
  await store.updateMeta(CASH_SESSION_META, (current) => {
    const session = current as LocalCashSession | undefined
    if (!session?.id) throw new CashSessionError('Abra o caixa antes.')
    const movement: LocalCashMovement = {
      id: uuidv7(now.getTime()), kind: input.kind, amountCents: check.amountCents, reason: check.reason, operatorId: input.operator.id, operatorName: input.operator.name, at,
      aboveExpected: input.kind === 'CashOut' && check.amountCents > drawerCents(session),
    }
    const next = { ...session, movements: [...session.movements, movement] }
    result = { session: next, movement }
    return next
  }, (next) => {
    const movement = (next as LocalCashSession).movements.at(-1)!
    return {
      kind: CASH_OUTBOX_KIND, at,
      payload: {
        type: input.kind === 'CashOut' ? 'CashOutRecorded' : 'CashInRecorded', sessionId: (next as LocalCashSession).id, movementId: movement.id,
        amountCents: movement.amountCents, reason: movement.reason, operatorId: movement.operatorId, aboveExpected: movement.aboveExpected, at,
      },
    }
  })
  return result!
}

/** Venda concluída na sessão: conta a venda e o dinheiro que ficou na gaveta (só para o painel, o alerta e o teto). */
export async function recordSaleInSession(store: LocalStore, tenders: readonly Pick<Tender, 'status' | 'methodType' | 'amountCents'>[]): Promise<LocalCashSession | null> {
  let updated: LocalCashSession | null = null
  await store.updateMeta(CASH_SESSION_META, (current) => {
    const session = current as LocalCashSession | undefined
    if (!session?.id) return current
    updated = { ...session, salesCount: session.salesCount + 1, cashSalesCents: session.cashSalesCents + cashKeptCents(tenders) }
    return updated
  })
  return updated
}

/** A última sessão fechada neste aparelho e quantas vezes ela tinha sido reaberta: a mesma reabertura não é retomada duas vezes (CX-02 CA 10). */
export const LAST_CLOSED_META = 'cash.lastClosed'

/** Uma linha da contagem (CX-02 CA 2): a forma (null = dinheiro sem forma cadastrada), o contado e, no dinheiro, as peças (opcional). */
export interface CashCountLine {
  methodId: string | null
  countedCents: Cents
  denominations?: { cents: Cents; quantity: number }[] | null
}

/** O que sai da contagem para o comprovante: o recolhimento ao Cofre, o fundo que fica e o contado por forma. Nunca esperado nem diferença. */
export interface CashCountResult {
  sessionId: string
  operatorName: string
  openedAt: string
  countedAt: string
  salesCount: number
  depositCents: Cents
  keptCents: Cents
  lines: CashCountLine[]
  movements: LocalCashMovement[]
}

/**
 * "Fechar caixa" (CX-02 CA 1): o turno passa a "em contagem" e o evento `CashCountStarted` vai para a fila. **Conferência cega (CA 3):** o
 * dinheiro calculado das vendas (`cashSalesCents`) é apagado do aparelho neste momento; a tela de contagem não tem de onde tirar o esperado.
 */
export async function startCashCount(store: LocalStore, operator: CashOperator, now = new Date()): Promise<LocalCashSession> {
  const at = now.toISOString()
  let result: LocalCashSession | null = null
  await store.updateMeta(CASH_SESSION_META, (current) => {
    const session = current as LocalCashSession | undefined
    if (!session?.id) throw new CashSessionError('Abra o caixa antes.')
    result = { ...session, counting: true, cashSalesCents: 0 }
    return result
  }, (next, previous) => ((previous as LocalCashSession | undefined)?.counting
    ? null
    : { kind: CASH_OUTBOX_KIND, at, payload: { type: 'CashCountStarted', sessionId: (next as LocalCashSession).id, operatorId: operator.id, at } }))
  return result!
}

/** Dinheiro contado: a linha da forma Dinheiro (ou sem forma). */
export const countedCashCents = (lines: readonly CashCountLine[], cashMethodIds: ReadonlySet<string>): Cents =>
  sumCents(lines.filter((l) => l.methodId === null || cashMethodIds.has(l.methodId)).map((l) => l.countedCents))

/** CX-02 CA 12: recolhimento = dinheiro contado − fundo que fica. */
export const depositCents = (countedCash: Cents, keptCents: Cents): Cents => Math.max(0, countedCash - keptCents)

/**
 * "Confirmar contagem" (CX-02 CA 2, 4 e 12): grava o evento `CashCounted` (o contado por forma, o fundo que fica, o recolhimento e quantas vendas o
 * aparelho concluiu) e encerra o turno neste aparelho, na mesma transação. Uma contagem só: depois dela, recontar é do supervisor. O fundo que
 * fica vira o padrão da próxima abertura.
 */
export async function confirmCashCount(store: LocalStore, input: { operator: CashOperator; lines: CashCountLine[]; keptCents: Cents; cashMethodIds: ReadonlySet<string> }, now = new Date()): Promise<CashCountResult> {
  const lines = input.lines.map((l) => ({ methodId: l.methodId, countedCents: l.countedCents, denominations: l.denominations?.length ? l.denominations : null }))
  if (lines.some((l) => !Number.isSafeInteger(l.countedCents) || l.countedCents < 0 || l.countedCents > MAX_AMOUNT_CENTS)) throw new CashSessionError('Valor contado inválido.')
  const cash = countedCashCents(lines, input.cashMethodIds)
  if (!Number.isSafeInteger(input.keptCents) || input.keptCents < 0 || input.keptCents > cash) throw new CashSessionError('O fundo que fica não pode passar do dinheiro contado.')
  const at = now.toISOString()
  let result: CashCountResult | null = null
  let reopenCount = 0
  await store.updateMeta(CASH_SESSION_META, (current) => {
    const session = current as LocalCashSession | undefined
    if (!session?.id) throw new CashSessionError('Abra o caixa antes.')
    reopenCount = session.reopenCount ?? 0
    result = {
      sessionId: session.id, operatorName: input.operator.name, openedAt: session.openedAt, countedAt: at, salesCount: session.salesCount,
      depositCents: depositCents(cash, input.keptCents), keptCents: input.keptCents, lines, movements: session.movements,
    }
    return null
  }, () => ({
    kind: CASH_OUTBOX_KIND, at,
    payload: {
      type: 'CashCounted', sessionId: result!.sessionId, countId: uuidv7(now.getTime()), operatorId: input.operator.id, at, keptCents: input.keptCents,
      depositMovementId: uuidv7(now.getTime()), salesCount: result!.salesCount, lines,
    },
  }))
  await store.setMeta(LAST_OPENING_META, input.keptCents)
  await store.setMeta(LAST_CLOSED_META, { id: result!.sessionId, reopenCount })
  return result!
}

/** A sessão reaberta na retaguarda (CX-02 CA 10), como chega no aperto de mão. */
export interface ReopenedCashSession {
  id: string
  operatorId: string
  openedAt: string
  openingCents: Cents
  reopenCount: number
}

/**
 * Retoma neste aparelho a sessão reaberta na retaguarda, se o aparelho não tem turno aberto e ainda não retomou esta reabertura. Volta sem o
 * dinheiro calculado (só o aviso e o teto o usam; a próxima contagem é cega do mesmo jeito).
 */
export async function adoptReopenedSession(store: LocalStore, reopened: ReopenedCashSession, operatorName: string): Promise<LocalCashSession | null> {
  const last = await store.getMeta<{ id: string; reopenCount: number }>(LAST_CLOSED_META)
  if (last && last.id === reopened.id && last.reopenCount >= reopened.reopenCount) return null
  let adopted: LocalCashSession | null = null
  await store.updateMeta(CASH_SESSION_META, (current) => {
    if (current && typeof (current as Partial<LocalCashSession>).id === 'string') return current
    adopted = {
      id: reopened.id, registerId: null, deviceEpoch: null, openedAt: reopened.openedAt, operatorId: reopened.operatorId, operatorName,
      openingCents: reopened.openingCents, salesCount: 0, cashSalesCents: 0, movements: [], reopenCount: reopened.reopenCount,
    }
    return adopted
  })
  return adopted
}
