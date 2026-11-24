import { UserRound } from 'lucide-react'
import { useCallback, useEffect, useState, useSyncExternalStore } from 'react'
import { approvePosSupervisorAction, endPosSession, listPosOperators, listPosSupervisors, loginPosPin, searchPosCustomers, useGetPosSession } from '@/api/generated/bemaqui'
import type { PosHandshake, PosOperator, PosOperators } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { getAccessToken, isPosPinSession, onSessionChange, setAccessToken, setSessionDeadline } from '@/api/session'
import { FloorButton, NumericKeypad } from '@/components/shopfloor/kit'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { signRequest } from '@/pdv/device-signing'
import { usePdv } from '@/pdv/pdv-context'
import { looksLikeDocument } from '@/pdv/sale/customer-document'
import { useOnline } from '@/pdv/use-online'
import { PIN_CHANGE_REQUIRED, pinBody } from '@/pdv/operator-pin'
import { registerLabel } from '@/pages/store/stores'
import { isPinComplete, PIN_LENGTH } from '@/pages/shopfloor/device'
import { PdvSale } from './sale/PdvSale'
import type { SupervisorApi } from './sale/supervisor'

const OPERATORS = '/api/v1/pos/sync/operators'
const PIN = '/api/v1/pos/sync/pin'

/**
 * Entrada do operador no caixa (/pdv, PDV-01 CA 11): o caixa já registrado mostra quem pode entrar (primeiro nome e inicial); o operador toca no
 * nome e digita o PIN. A lista e a entrada vão assinadas pela chave do aparelho (esquema Device), sem sessão de usuário. O PIN inicial que o gerente
 * deu ao usuário de operação vale só para a primeira entrada: a tela pede o PIN novo. Com a sessão aberta, a tela de venda (PDV-02).
 */
export function PdvOperatorEntry({ keys, handshake, onDeviceGone }: { keys: CryptoKeyPair; handshake: PosHandshake; onDeviceGone: () => void }) {
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  if (token && isPosPinSession(token)) return <OperatorSession handshake={handshake} onDeviceGone={onDeviceGone} />
  return <OperatorList keys={keys} onDeviceGone={onDeviceGone} />
}

type OperatorState = { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'ready'; data: PosOperators }

/** Lista assinada pela chave do aparelho; 401 quer dizer que o aparelho deixou de ser caixa ('gone'). */
async function fetchOperators(keys: CryptoKeyPair): Promise<OperatorState | 'gone'> {
  try {
    return { kind: 'ready', data: await listPosOperators({ headers: await signRequest(keys, 'GET', OPERATORS) }) }
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return 'gone'
    return { kind: 'error', message: e instanceof ApiError ? e.message : 'Não foi possível falar com o servidor.' }
  }
}

function OperatorList({ keys, onDeviceGone }: { keys: CryptoKeyPair; onDeviceGone: () => void }) {
  const [state, setState] = useState<OperatorState>({ kind: 'loading' })
  const [selected, setSelected] = useState<PosOperator | null>(null)

  const apply = useCallback((next: OperatorState | 'gone') => {
    if (next === 'gone') onDeviceGone()
    else setState(next)
  }, [onDeviceGone])

  useEffect(() => {
    let alive = true
    void fetchOperators(keys).then((next) => { if (alive) apply(next) })
    return () => { alive = false }
  }, [keys, apply])

  const reload = () => {
    setState({ kind: 'loading' })
    void fetchOperators(keys).then(apply)
  }

  if (state.kind === 'loading') return <Skeleton className="h-40" />
  if (state.kind === 'error') return <ErrorState title="Não foi possível carregar os operadores." message={state.message} onRetry={reload} />
  if (selected) return <PinPad keys={keys} operator={selected} onBack={() => { setSelected(null); reload() }} onDeviceGone={onDeviceGone} />

  const { data } = state
  return (
    <section className="space-y-4 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">Toque no seu nome e digite o seu PIN</h2>
      {data.operators.length === 0 ? (
        <p className="text-muted-foreground">
          Ninguém tem PIN para este caixa ainda. O gerente cria o usuário de operação em Usuários e acessos, ou a pessoa cria o PIN do caixa em Meu perfil → Segurança.
        </p>
      ) : (
        <ul aria-label="Quem pode entrar neste caixa" className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {data.operators.map((o) => (
            <li key={o.userId}>
              <FloorButton tone="secondary" className="w-full justify-start py-3 text-[20px]" onClick={() => setSelected(o)}>
                <UserRound aria-hidden="true" className="size-7" />{o.displayName}
              </FloorButton>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * Cliente cadastrado para a venda (PDV-02 CA 15): só com rede, pela sessão por PIN do caixa; nome e documento já mascarado. Termo com cara de
 * CPF ou CNPJ (11 dígitos ou mais) não sai do aparelho, para o documento nunca ir na URL (revisão de segurança, achado 2).
 */
const searchCustomers = async (term: string) => (looksLikeDocument(term) ? [] : searchPosCustomers({ term }))

/** PDV-04: "Chamar supervisor" pela sessão por PIN do operador (o PIN do supervisor é conferido no servidor, neste aparelho). */
const supervisorApi: SupervisorApi = {
  list: () => listPosSupervisors(),
  approve: (input) => approvePosSupervisorAction(input),
}

/** PIN no teclado grande; se o servidor pedir, o PIN novo (duas vezes) na mesma tela. */
function PinPad({ keys, operator, onBack, onDeviceGone }: { keys: CryptoKeyPair; operator: PosOperator; onBack: () => void; onDeviceGone: () => void }) {
  const [pin, setPin] = useState('')
  const [newPin, setNewPin] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [changing, setChanging] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const step = !changing ? 'pin' : newPin.length < PIN_LENGTH ? 'new' : 'confirm'

  const enter = async () => {
    if (changing && newPin !== confirmation) {
      setError('Os dois PINs não são iguais. Digite de novo.')
      setNewPin('')
      setConfirmation('')
      return
    }

    setBusy(true)
    setError(null)
    const input = pinBody(operator.userId, pin, changing ? newPin : undefined)
    try {
      const session = await loginPosPin(input, { headers: await signRequest(keys, 'POST', PIN, JSON.stringify(input)) })
      setAccessToken(session.accessToken)
      setSessionDeadline(session.sessionExpiresAt)
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) { onDeviceGone(); return }
      if (e instanceof ApiError && e.code === PIN_CHANGE_REQUIRED) {
        setChanging(true)
        setError(null)
        return
      }

      if (changing && e instanceof ApiError && e.status === 422 && e.fields.newPin) {
        setNewPin('')
        setConfirmation('')
      } else {
        setPin('')
        setChanging(false)
      }

      setError(e instanceof ApiError ? e.message : 'Não foi possível entrar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const value = step === 'pin' ? pin : step === 'new' ? newPin : confirmation
  const change = step === 'pin' ? setPin : step === 'new' ? setNewPin : setConfirmation
  const ready = step === 'pin' ? isPinComplete(pin) : step === 'confirm' && isPinComplete(confirmation)
  return (
    <section className="space-y-4 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">Olá, {operator.displayName}</h2>
      <p className="text-muted-foreground">
        {step === 'pin' ? 'Digite o seu PIN de 6 números.' : step === 'new' ? 'Crie o seu PIN: 6 números, sem sequência nem repetição. O PIN que o gerente deu vale só para a primeira entrada.' : 'Digite o PIN novo de novo, para conferir.'}
      </p>
      <NumericKeypad label={step === 'pin' ? 'PIN' : step === 'new' ? 'PIN novo' : 'Confirme o PIN novo'} mask max={PIN_LENGTH} value={value} onChange={(v) => { change(v); setError(null) }} />
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
      <div className="flex gap-4">
        <FloorButton tone="secondary" className="flex-1" onClick={onBack} disabled={busy}>Voltar</FloorButton>
        <FloorButton className="flex-1" onClick={() => void enter()} disabled={busy || !ready}>{busy ? 'Entrando…' : 'Entrar'}</FloorButton>
      </div>
    </section>
  )
}

/**
 * Operador no caixa: a tela de venda (PDV-02), com "Sair" (a sessão cai no servidor; o PIN continua). Quem está no caixa e as ações dele
 * (pos.cancel_item) vêm da sessão; a venda em si só fala com o LocalStore. Fora do app do caixa (sem LocalStore), só quem está no caixa.
 */
function OperatorSession({ handshake, onDeviceGone }: { handshake: PosHandshake; onDeviceGone: () => void }) {
  const pdv = usePdv()
  const online = useOnline()
  const session = useGetPosSession({ query: { retry: false, staleTime: 0 } })
  const [busy, setBusy] = useState(false)
  const error = session.error instanceof ApiError ? session.error : null

  useEffect(() => {
    if (error?.status === 401 && error.code === 'device_not_registered') onDeviceGone()
  }, [error, onDeviceGone])

  const leave = async () => {
    setBusy(true)
    try {
      await endPosSession()
    } catch {
      // a sessão pode já ter caído: sai do mesmo jeito
    } finally {
      setAccessToken(null)
      setBusy(false)
    }
  }

  if (session.isPending) return <Skeleton className="h-40" />
  if (session.isError || !session.data) {
    return <ErrorState title="Não foi possível conferir quem está no caixa." message={error?.message} onRetry={() => void session.refetch()} />
  }

  const s = session.data
  if (pdv) {
    return (
      <PdvSale store={pdv.store} handshake={handshake} online={online} searchCustomers={searchCustomers} onLeave={() => void leave()}
        supervisor={supervisorApi}
        operator={{
          id: s.userId, name: s.name, canCancelItem: s.actions.includes('cancel_item'), canCashOut: s.actions.includes('cash_out'), canCashIn: s.actions.includes('cash_in'),
          canDiscount: s.actions.includes('discount'), canChangePrice: s.actions.includes('change_price'), canCancelSale: s.actions.includes('cancel_sale'),
        }} />
    )
  }

  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">{s.name} está no caixa {registerLabel(s.registerNumber)} da loja {s.storeName}.</h2>
      <p className="text-muted-foreground">
        A sessão vale até {new Date(s.sessionExpiresAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}. A tela de venda abre no app do caixa.
      </p>
      <FloorButton tone="secondary" onClick={() => void leave()} disabled={busy}>{busy ? 'Saindo…' : 'Sair'}</FloorButton>
    </section>
  )
}
