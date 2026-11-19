import { useState } from 'react'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { CashSessionError, type LocalCashMovement, type LocalCashSession, recordMovement } from '@/pdv/cash/cash-session'
import type { Cents } from '@/pdv/money'
import { CashMovementSheet, CashPanel, CashReceiptSheet, NO_PERMISSION } from './CashScreens'

export interface CashOperator {
  id: string
  name: string
  /** `pos.cash_out` (sangria). */
  canCashOut: boolean
  /** `pos.cash_in` (suprimento). */
  canCashIn: boolean
}

/**
 * Área do caixa no app (CX-01): o painel (CA 10), a sangria e o suprimento com o comprovante (CA 4 e 5) e a permissão de cada um (CA 6: sem ela,
 * "Você não tem permissão para esta ação."; o "Chamar supervisor" é da PDV-04). Grava só no aparelho, pela porta `LocalStore`.
 */
export function CashArea({ store, session, operator, place, startWith = null, onSession, onBack, onClose }: {
  store: LocalStore
  session: LocalCashSession
  operator: CashOperator
  /** "Loja Centro · Caixa 01", no comprovante. */
  place: string
  startWith?: 'CashOut' | 'CashIn' | null
  onSession: (session: LocalCashSession) => void
  onBack: () => void
  onClose: () => void
}) {
  const allowed = (kind: 'CashOut' | 'CashIn') => (kind === 'CashOut' ? operator.canCashOut : operator.canCashIn)
  const [kind, setKind] = useState<'CashOut' | 'CashIn' | null>(() => (startWith && allowed(startWith) ? startWith : null))
  const [notice, setNotice] = useState<{ tone: 'error' | 'ok'; text: string } | null>(() => (startWith && !allowed(startWith) ? { tone: 'error', text: NO_PERMISSION } : null))
  const [receipt, setReceipt] = useState<LocalCashMovement | null>(null)

  const start = (next: 'CashOut' | 'CashIn') => {
    if (!allowed(next)) { setNotice({ tone: 'error', text: NO_PERMISSION }); return }
    setNotice(null)
    setKind(next)
  }

  const confirm = async (amountCents: Cents, reason: string) => {
    if (!kind) return
    try {
      const done = await recordMovement(store, { kind, amountCents, reason, operator })
      onSession(done.session)
      setKind(null)
      setReceipt(done.movement)
      setNotice({ tone: 'ok', text: kind === 'CashOut' ? 'Sangria registrada.' : 'Suprimento registrado.' })
    } catch (e) {
      if (e instanceof CashSessionError) { setKind(null); setNotice({ tone: 'error', text: e.message }); return }
      throw e
    }
  }

  return (
    <>
      <CashPanel session={session} notice={notice} onBack={onBack} onMovement={start} onClose={onClose} />
      <CashMovementSheet kind={kind} onCancel={() => setKind(null)} onConfirm={confirm} />
      <CashReceiptSheet movement={receipt} place={place} onDone={() => setReceipt(null)} />
    </>
  )
}
