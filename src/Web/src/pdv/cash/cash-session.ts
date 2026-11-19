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

/**
 * Fecha a sessão neste aparelho com o evento `CashSessionClosed`. **Provisório até a CX-02**, que traz a contagem e a conferência cega: aqui só se
 * encerra o turno (as vendas em espera já foram tratadas pela tela).
 */
export async function closeCashSession(store: LocalStore, operator: CashOperator, now = new Date()): Promise<void> {
  const at = now.toISOString()
  await store.updateMeta(CASH_SESSION_META, () => null, (_next, previous) => {
    const session = previous as LocalCashSession | undefined
    return session?.id ? { kind: CASH_OUTBOX_KIND, at, payload: { type: 'CashSessionClosed', sessionId: session.id, operatorId: operator.id, at } } : null
  })
}
