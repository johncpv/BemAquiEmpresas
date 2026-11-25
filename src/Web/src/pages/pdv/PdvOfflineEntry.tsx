import { CloudOff, UserRound } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { PosHandshake } from '@/api/generated/model'
import { FloorButton, NumericKeypad } from '@/components/shopfloor/kit'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { loadCashSession } from '@/pdv/cash/cash-session'
import { loadVerifierKey } from '@/pdv/device-signing'
import type { LocalStore } from '@/pdv/local-store/local-store'
import {
  endOfflineSession, loadOfflineSession, loadVerifiers, NOT_RECEIVED_MESSAGE, OfflineAccessError, offlineApprove, offlineLogin, type OfflineSession, reportClockBehind, touchClock,
  windowHoursOf,
} from '@/pdv/offline/offline-access'
import type { OfflineVerifier } from '@/pdv/offline/offline-crypto'
import { NEEDS_NETWORK, OFFLINE_DISABLED } from '@/pdv/offline/offline-features'
import { openBlockedMessage, windowState } from '@/pdv/offline/offline-window'
import { usePdv } from '@/pdv/pdv-context'
import { isPinComplete, PIN_LENGTH } from '@/pages/shopfloor/device'
import { PdvSale, type SaleOperator } from './sale/PdvSale'
import type { SupervisorAction, SupervisorApi } from './sale/supervisor'

const CHECK_EVERY_MS = 60_000

type Load = { kind: 'loading' } | { kind: 'error' } | { kind: 'ready'; verifiers: OfflineVerifier[]; session: OfflineSession | null }

/**
 * Caixa sem rede (OFF-03): a lista dos operadores da loja e o teclado do PIN, conferido no aparelho contra o verificador cifrado (CA 2); quem ainda
 * não chegou a este caixa vê "Este caixa ainda não recebeu o seu cadastro." (CA 5); 5 erros bloqueiam a pessoa neste aparelho (CA 3). Com a
 * entrada aceita, a tela de venda roda com a sessão marcada como offline e o supervisor aprova com o código sem internet (CA 7).
 */
export function PdvOfflineEntry({ handshake, onSessionChange }: { handshake: PosHandshake; onSessionChange: (active: boolean) => void }) {
  const pdv = usePdv()
  const store = pdv?.store ?? null
  const [load, setLoad] = useState<Load>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (!store) return
    let alive = true
    void Promise.all([loadVerifierKey().then((key) => loadVerifiers(store, key)), loadOfflineSession(store)])
      .then(([verifiers, session]) => { if (alive) setLoad({ kind: 'ready', verifiers, session }) })
      .catch(() => { if (alive) setLoad({ kind: 'error' }) })
    return () => { alive = false }
  }, [store, attempt])

  const enter = useCallback((session: OfflineSession | null) => {
    setLoad((l) => (l.kind === 'ready' ? { ...l, session } : l))
    onSessionChange(!!session)
  }, [onSessionChange])

  if (!store) return <ErrorState title="O caixa sem rede só abre no app do caixa." />
  if (load.kind === 'loading') return <Skeleton className="h-40" />
  if (load.kind === 'error') return <ErrorState title="Não foi possível ler os dados guardados neste aparelho." onRetry={() => setAttempt((a) => a + 1)} />
  if (load.session) {
    return <OfflineSale store={store} handshake={handshake} session={load.session} verifiers={load.verifiers} onLeave={() => void endOfflineSession(store).then(() => enter(null))} />
  }

  return <OperatorPicker store={store} handshake={handshake} verifiers={load.verifiers} onEnter={enter} />
}

function OperatorPicker({ store, handshake, verifiers, onEnter }: { store: LocalStore; handshake: PosHandshake; verifiers: OfflineVerifier[]; onEnter: (s: OfflineSession) => void }) {
  const [selected, setSelected] = useState<{ userId: string; displayName: string } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const operators = useMemo(() => {
    const list = store.list('operators').map((o) => ({ userId: o.id, displayName: o.displayName }))
    for (const v of verifiers) if (v.kind === 'operator' && !list.some((o) => o.userId === v.userId)) list.push({ userId: v.userId, displayName: v.displayName })
    return list
  }, [store, verifiers])
  const verifierOf = (userId: string) => verifiers.find((v) => v.userId === userId && v.kind === 'operator') ?? null

  if (selected) {
    return <OfflinePinPad store={store} deviceId={handshake.deviceId} operator={selected} verifier={verifierOf(selected.userId)} onBack={() => setSelected(null)} onEnter={onEnter} />
  }

  return (
    <section className="space-y-4 rounded-md border border-border bg-card p-5">
      <h2 className="flex items-center gap-2 text-lg font-semibold"><CloudOff aria-hidden="true" className="size-6" />Sem conexão: toque no seu nome e digite o seu PIN</h2>
      {operators.length === 0 ? (
        <p className="text-muted-foreground">Este caixa ainda não recebeu o cadastro de ninguém. Conecte à internet uma vez para baixar quem pode entrar.</p>
      ) : (
        <ul aria-label="Quem pode entrar neste caixa" className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {operators.map((o) => (
            <li key={o.userId}>
              <FloorButton tone="secondary" className="w-full justify-start py-3 text-[20px]" onClick={() => { setNotice(null); setSelected(o) }}>
                <UserRound aria-hidden="true" className="size-7" />{o.displayName}
              </FloorButton>
            </li>
          ))}
        </ul>
      )}
      <FloorButton tone="secondary" onClick={() => setNotice(NOT_RECEIVED_MESSAGE)}>Não achei o meu nome</FloorButton>
      {notice ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">{notice}</p> : null}
      <OfflineMenu />
    </section>
  )
}

function OfflinePinPad({ store, deviceId, operator, verifier, onBack, onEnter }: {
  store: LocalStore
  deviceId: string
  operator: { userId: string; displayName: string }
  verifier: OfflineVerifier | null
  onBack: () => void
  onEnter: (s: OfflineSession) => void
}) {
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(verifier ? null : NOT_RECEIVED_MESSAGE)
  const [busy, setBusy] = useState(false)
  const enter = async () => {
    setBusy(true)
    setError(null)
    try {
      const result = await offlineLogin(store, { verifier, pin, deviceId })
      if (result.ok) onEnter(result.value)
      else { setPin(''); setError(result.message) }
    } catch {
      setPin('')
      setError('Não foi possível conferir o PIN neste aparelho. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="space-y-4 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">Olá, {operator.displayName}</h2>
      <p className="text-muted-foreground">Sem conexão: o PIN é conferido neste aparelho. Digite o seu PIN de 6 números.</p>
      {verifier ? <NumericKeypad label="PIN" mask max={PIN_LENGTH} value={pin} onChange={(v) => { setPin(v); setError(null) }} /> : null}
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
      <div className="flex gap-4">
        <FloorButton tone="secondary" className="flex-1" onClick={onBack} disabled={busy}>Voltar</FloorButton>
        {verifier ? <FloorButton className="flex-1" onClick={() => void enter()} disabled={busy || !isPinComplete(pin)}>{busy ? 'Conferindo…' : 'Entrar'}</FloorButton> : null}
      </div>
    </section>
  )
}

/** CA 11: o que precisa de internet, desabilitado e com o motivo. */
export function OfflineMenu() {
  return (
    <details className="rounded-md border border-border p-3">
      <summary className="cursor-pointer font-semibold">Só com internet ({OFFLINE_DISABLED.length})</summary>
      <ul aria-label="Disponível só com internet" className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2">
        {OFFLINE_DISABLED.map((item) => (
          <li key={item}>
            <FloorButton tone="secondary" disabled className="w-full flex-col items-start py-2 text-[17px]">
              <span>{item}</span>
              <span className="flex items-center gap-1 text-[14px] font-normal"><CloudOff aria-hidden="true" className="size-4" />{NEEDS_NETWORK}</span>
            </FloorButton>
          </li>
        ))}
      </ul>
    </details>
  )
}

/** A tela de venda com a entrada sem rede: ações da descida, supervisor com o código sem internet e o bloqueio da janela na abertura (CA 9 e 14). */
function OfflineSale({ store, handshake, session, verifiers, onLeave }: {
  store: LocalStore
  handshake: PosHandshake
  session: OfflineSession
  verifiers: OfflineVerifier[]
  onLeave: () => void
}) {
  const [openBlocked, setOpenBlocked] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    const check = async () => {
      const now = new Date()
      const clock = store.writable ? await touchClock(store, now).catch(() => null) : null
      const state = windowState(clock, windowHoursOf(store), now)
      if (state.kind === 'clock-behind' && store.writable) await reportClockBehind(store, state.floor, now).catch(() => undefined)
      if (alive) setOpenBlocked(openBlockedMessage(state))
    }
    void check()
    const timer = setInterval(() => void check(), CHECK_EVERY_MS)
    return () => { alive = false; clearInterval(timer) }
  }, [store])

  const operator = useMemo<SaleOperator>(() => ({
    id: session.userId, name: session.name, canCancelItem: session.actions.includes('cancel_item'), canCashOut: session.actions.includes('cash_out'),
    canCashIn: session.actions.includes('cash_in'), canDiscount: session.actions.includes('discount'), canChangePrice: false, canCancelSale: session.actions.includes('cancel_sale'),
  }), [session])

  const supervisor = useMemo<SupervisorApi>(() => ({
    offline: true,
    list: async () => verifiers.filter((v) => v.kind === 'supervisor' && v.userId !== session.userId)
      .map((v) => ({ userId: v.userId, displayName: v.displayName, actions: v.actions })),
    approve: async (input) => {
      const cash = await loadCashSession(store)
      const result = await offlineApprove(store, {
        verifier: verifiers.find((v) => v.kind === 'supervisor' && v.userId === input.supervisorId) ?? null, code: input.pin, deviceId: handshake.deviceId,
        operatorId: session.userId, cashSessionId: cash?.id ?? null, limits: store.catalogState()?.storeSettings?.offlineLimits ?? null, action: input.action as SupervisorAction,
        reason: input.reason, saleId: input.saleId, lineId: input.lineId, beforeCents: Math.round(input.beforeAmount * 100), afterCents: Math.round(input.afterAmount * 100),
      })
      if (!result.ok) throw new OfflineAccessError(result.message)
      return result.value
    },
  }), [store, verifiers, session, handshake.deviceId])

  return (
    <div className="space-y-3">
      <PdvSale store={store} handshake={handshake} operator={operator} online={false} searchCustomers={null} onLeave={onLeave} supervisor={supervisor} offlineMode openBlocked={openBlocked} />
      <OfflineMenu />
    </div>
  )
}
