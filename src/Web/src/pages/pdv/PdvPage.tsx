import { useNavigate, useSearch } from '@tanstack/react-router'
import { CircleHelp, CloudOff } from 'lucide-react'
import { type FormEvent, type ReactNode, useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { posHandshake, registerPosDevice, useListStores } from '@/api/generated/bemaqui'
import type { PosHandshake } from '@/api/generated/model'
import { ApiError, OFFLINE_CODE } from '@/api/http'
import { getAccessToken, isPosPinSession, onSessionChange } from '@/api/session'
import { ensureSession } from '@/auth/session-guard'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { FloorButton } from '@/components/shopfloor/kit'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { CATALOG_EMPTY_HINT, CATALOG_EMPTY_TITLE, CATALOG_LOADING_TITLE, FIRST_OPEN_OFFLINE_HINT, FIRST_OPEN_OFFLINE_TITLE } from '@/pdv/catalog-screen'
import { createDeviceKey, deviceSupported, forgetDeviceKey, loadDeviceKey, signRequest } from '@/pdv/device-signing'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { usePdv } from '@/pdv/pdv-context'
import { SUPPORTED_BROWSERS, UNSUPPORTED_TITLE } from '@/pdv/platform'
import { useCatalogSync } from '@/pdv/use-catalog-sync'
import { useOnline } from '@/pdv/use-online'
import { registerLabel } from '@/pages/store/stores'
import { loadOfflineSession } from '@/pdv/offline/offline-access'
import { NEEDS_NETWORK } from '@/pdv/offline/offline-features'
import { PdvOfflineEntry } from './PdvOfflineEntry'
import { PdvOperatorEntry } from './PdvOperatorEntry'

const HANDSHAKE = '/api/v1/pos/sync/handshake'

type State =
  | { kind: 'checking' }
  | { kind: 'unsupported' }
  | { kind: 'unregistered' }
  | { kind: 'blocked'; message: string }
  | { kind: 'error'; message: string }
  | { kind: 'clock'; message: string; serverTime: string | null }
  | { kind: 'first-offline' }
  | { kind: 'registered'; register: PosHandshake; keys: CryptoKeyPair; offline: boolean }

/** Último aperto de mão que deu certo, guardado no LocalStore para o caixa abrir sem rede mostrando o caixa e a loja (OFF-01 CA 2). */
type SavedRegister = { register: PosHandshake; at: string }

/**
 * Aperto de mão assinado pela chave do aparelho (ADR 0048 §2): diz se este aparelho é um caixa e de qual loja. Sem rede (OFF-01), vale o último
 * aperto de mão guardado no aparelho; sem ele, é a primeira abertura e ela precisa de internet.
 */
async function check(store: LocalStore | null): Promise<State> {
  if (!deviceSupported()) return { kind: 'unsupported' }
  const keys = await loadDeviceKey()
  if (!keys) return { kind: 'unregistered' }
  try {
    const register = await posHandshake({ headers: await signRequest(keys, 'POST', HANDSHAKE) })
    if (store?.writable) await store.setMeta('register', { register, at: new Date().toISOString() } satisfies SavedRegister).catch(() => { /* a tela segue com rede */ })
    return { kind: 'registered', register, keys, offline: false }
  } catch (e) {
    if (e instanceof ApiError && e.status === 0 && e.code === OFFLINE_CODE) {
      const saved = await store?.getMeta<SavedRegister>('register').catch(() => undefined)
      return saved ? { kind: 'registered', register: saved.register, keys, offline: true } : { kind: 'first-offline' }
    }

    if (e instanceof ApiError && e.status === 401) {
      // Relógio do aparelho fora da tolerância (revisão do PR 227, B5): acertar o relógio, não registrar de novo.
      if (e.code === 'device_clock_skew') return { kind: 'clock', message: e.message, serverTime: typeof e.data.serverTime === 'string' ? e.data.serverTime : null }
      return e.code === 'device_revoked' ? { kind: 'blocked', message: e.message } : { kind: 'unregistered' }
    }

    return { kind: 'error', message: e instanceof ApiError ? e.message : 'Não foi possível falar com o servidor.' }
  }
}

/**
 * Caixa (/pdv, PDV-01): o registro do aparelho e a entrada do operador por PIN. Aparelho não registrado: "Este aparelho ainda não é um caixa." com
 * "Registrar este aparelho" (pede login completo, pos.manage_devices e o código do autenticador). Registrado: a lista de quem pode entrar e o PIN
 * (CA 11). A tela de venda chega na PDV-02, sobre a OFF-01.
 *
 * OFF-01: roda dentro do app do caixa (src/pdv-app, build próprio em /pdv/). Sem rede, abre com o caixa e a loja do último aperto de mão guardado
 * no `LocalStore`; a etapa do catálogo (foto com progresso, vazio, primeira abertura sem rede) fica no `CatalogGate`.
 */
export function PdvPage() {
  const search = useSearch({ strict: false }) as { registrar?: string }
  const navigate = useNavigate()
  const pdv = usePdv()
  const store = pdv?.store ?? null
  const online = useOnline()
  const [state, setState] = useState<State>({ kind: 'checking' })
  const [registering, setRegistering] = useState(false)
  // Operador no caixa (sessão por PIN): a tela de venda ocupa a largura toda.
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  // OFF-03: entrada sem rede em curso (guardada no aparelho): a tela de venda continua nela até o operador sair, mesmo com a rede de volta (CA 12).
  const [offlineActive, setOfflineActive] = useState(false)
  useEffect(() => {
    let alive = true
    if (store) void loadOfflineSession(store).then((s) => { if (alive) setOfflineActive(!!s) }).catch(() => undefined)
    return () => { alive = false }
  }, [store])
  const offlineEntry = state.kind === 'registered' && (state.offline || offlineActive)
  const selling = state.kind === 'registered' && ((!state.offline && !!token && isPosPinSession(token)) || offlineActive)

  const startRegister = useCallback(async () => {
    if (await ensureSession()) {
      setRegistering(true)
      return
    }

    window.location.assign('/entrar?volta=' + encodeURIComponent('/pdv/?registrar=1'))
  }, [])

  const refresh = useCallback(() => {
    setState({ kind: 'checking' })
    void check(store).then(setState)
  }, [store])

  // Na abertura: confere o aparelho; voltando do login com ?registrar=1, abre o registro direto.
  const wantsRegister = search.registrar === '1'
  useEffect(() => {
    let alive = true
    void check(store).then(async (next) => {
      if (!alive) return
      setState(next)
      if (wantsRegister && (next.kind === 'unregistered' || next.kind === 'blocked')) {
        await navigate({ to: '/pdv', search: {}, replace: true })
        await startRegister()
      }
    })
    return () => { alive = false }
  }, [wantsRegister, navigate, startRegister, store])

  // Aberto sem rede (com o aperto de mão guardado ou na primeira abertura): quando a rede volta, confere de novo no servidor. Só na volta da
  // rede, para não repetir sem parar quando o navegador diz "com rede" e a internet não responde.
  const offlineView = (state.kind === 'registered' && state.offline) || state.kind === 'first-offline'
  const wasOnline = useRef(online)
  useEffect(() => {
    const cameBack = online && !wasOnline.current
    wasOnline.current = online
    if (cameBack && offlineView) refresh()
  }, [online, offlineView, refresh])

  return (
    <main id="conteudo" data-surface="floor" className={selling ? 'mx-auto min-h-dvh max-w-screen-2xl space-y-3 bg-background p-3 text-foreground sm:p-4' : 'mx-auto min-h-dvh max-w-2xl space-y-6 bg-background p-4 text-foreground sm:p-8'}>
      <header className="flex items-start gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Caixa (PDV)</p>
          <h1 className="font-display text-2xl font-bold">{state.kind === 'registered' ? `Caixa ${registerLabel(state.register.registerNumber)} · ${state.register.storeName}` : 'Este aparelho'}</h1>
        </div>
        {pdv ? (
          <FloorButton tone="secondary" onClick={pdv.openHelp} aria-label="Ajuda do caixa" className="px-3">
            <CircleHelp aria-hidden="true" className="size-6" />
            <span className="max-sm:sr-only">Ajuda</span>
          </FloorButton>
        ) : null}
      </header>
      {registering ? (
        <RegisterForm onDone={() => { setRegistering(false); void refresh() }} onCancel={() => setRegistering(false)} />
      ) : state.kind === 'checking' ? <Skeleton className="h-40" /> : state.kind === 'unsupported' ? (
        <Card title={UNSUPPORTED_TITLE} text={`Use um destes: ${SUPPORTED_BROWSERS.join(', ')}. iPhone e iPad ficam fora do caixa.`} />
      ) : state.kind === 'first-offline' ? (
        <Card title={FIRST_OPEN_OFFLINE_TITLE} text={FIRST_OPEN_OFFLINE_HINT}
          action={<button type="button" onClick={() => void refresh()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Tentar de novo</button>} />
      ) : state.kind === 'clock' ? (
        <Card title="Acerte o relógio deste aparelho."
          text={`A data e a hora deste aparelho estão diferentes das do servidor${state.serverTime ? ` (agora: ${new Date(state.serverTime).toLocaleString('pt-BR')})` : ''}. Acerte o relógio nas configurações do aparelho e confira de novo.`}
          action={<button type="button" onClick={() => void refresh()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Conferir de novo</button>} />
      ) : state.kind === 'error' ? (
        <ErrorState title="Não foi possível conferir o aparelho." message={state.message} onRetry={() => void refresh()} />
      ) : state.kind === 'registered' ? (
        <CatalogGate keys={state.keys} online={online}>
          {offlineEntry ? (
            pdv ? <PdvOfflineEntry handshake={state.register} onSessionChange={setOfflineActive} /> : (
              <Card title="Sem conexão." icon={<CloudOff aria-hidden="true" className="size-6" />}
                text="O caixa abriu com os dados guardados neste aparelho. Sem rede, a entrada por PIN funciona no app do caixa."
                action={<button type="button" onClick={() => void refresh()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Tentar de novo</button>} />
            )
          ) : <PdvOperatorEntry keys={state.keys} handshake={state.register} onDeviceGone={refresh} />}
          {selling ? null : <p className="text-sm text-muted-foreground">As vendas deste aparelho vão sair numeradas a partir de {state.register.salePrefix}000001.</p>}
          {selling ? null : online && !state.offline ? (
            <p className="text-sm"><a href="/pdv/config/impressora/" className="font-semibold text-primary underline">Impressora do caixa</a>: instale e teste a impressora do cupom.</p>
          ) : (
            <p className="flex items-center gap-1 text-sm text-muted-foreground"><CloudOff aria-hidden="true" className="size-4" />Impressora do caixa (instalação de impressora nova): {NEEDS_NETWORK}</p>
          )}
        </CatalogGate>
      ) : (
        <Card title={state.kind === 'blocked' ? 'Este aparelho foi bloqueado ou trocado por outro.' : 'Este aparelho ainda não é um caixa.'}
          text={state.kind === 'blocked' ? 'Para voltar a vender nele, registre o aparelho de novo.' : 'Registre o aparelho para ele vender por um caixa da loja. É preciso entrar com e-mail e senha de quem pode registrar aparelhos.'}
          action={<button type="button" onClick={() => void startRegister()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Registrar este aparelho</button>} />
      )}
    </main>
  )
}

function Card({ title, text, action, icon }: { title: string; text: string; action?: ReactNode; icon?: ReactNode }) {
  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-5">
      <h2 className="flex items-center gap-2 text-lg font-semibold">{icon}{title}</h2>
      <p className="text-muted-foreground">{text}</p>
      {action}
    </section>
  )
}

/**
 * Etapa do catálogo (OFF-01, "Telas e estados"): na primeira abertura, "Preparando o caixa: baixando produtos" com o progresso; sem produto,
 * "Nenhum produto disponível para esta loja."; primeira abertura sem rede, "A primeira abertura precisa de internet.". Com o catálogo guardado,
 * a tela abre na hora e o delta roda por trás. Sem a fonte do catálogo ligada (até o adaptador do cliente Orval), a etapa não aparece.
 */
function CatalogGate({ keys, online, children }: { keys: CryptoKeyPair; online: boolean; children: ReactNode }) {
  const { screen, retry } = useCatalogSync(keys, online)
  const retryButton = <button type="button" onClick={retry} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Tentar de novo</button>
  if (screen.kind === 'loading') {
    return (
      <section aria-busy="true" className="space-y-3 rounded-md border border-border bg-card p-5">
        <h2 className="text-lg font-semibold">{CATALOG_LOADING_TITLE}</h2>
        <progress value={screen.percent} max={100} aria-label={CATALOG_LOADING_TITLE} className="h-3 w-full accent-primary" />
        <p className="text-muted-foreground">{screen.percent}%. Só na primeira vez: depois, o caixa abre na hora, mesmo sem internet.</p>
      </section>
    )
  }

  if (screen.kind === 'offline-first') return <Card title={FIRST_OPEN_OFFLINE_TITLE} text={FIRST_OPEN_OFFLINE_HINT} action={retryButton} />
  if (screen.kind === 'error') return <ErrorState title="Não foi possível baixar os produtos." message={screen.message} onRetry={retry} />
  if (screen.kind === 'empty') {
    return <Card title={CATALOG_EMPTY_TITLE} text={CATALOG_EMPTY_HINT} action={<button type="button" onClick={retry} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Conferir de novo</button>} />
  }

  return <>{children}</>
}

/** Registro (CA 4 e 6): loja, caixa e nome; a chave nasce aqui e só a pública vai ao servidor. */
function RegisterForm({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const stores = useListStores()
  const active = useMemo(() => (stores.data ?? []).filter((s) => s.isActive && s.registers.some((r) => r.isActive)), [stores.data])
  const [storeId, setStoreId] = useState('')
  const [registerId, setRegisterId] = useState('')
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [replace, setReplace] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const store = active.find((s) => s.id === storeId) ?? active[0]
  const registers = (store?.registers ?? []).filter((r) => r.isActive)
  const register = registers.find((r) => r.id === registerId) ?? registers.find((r) => !r.device) ?? registers[0]

  const submit = async (confirmReplace: boolean) => {
    if (!store || !register) { setError('Escolha a loja e o caixa.'); return }
    const deviceName = name.trim() || `Caixa ${registerLabel(register.number)}`
    setBusy(true)
    setError(null)
    try {
      const { publicKey } = await createDeviceKey()
      await registerPosDevice({ name: deviceName, kind: 'Register', storeId: store.id, registerId: register.id, publicKey, replaceActive: confirmReplace })
      onDone()
    } catch (e) {
      await forgetDeviceKey()
      if (e instanceof ApiError && e.code === 'register_has_device') setReplace(e.message)
      else setError(e instanceof ApiError ? e.message : 'Não foi possível registrar o aparelho.')
    } finally {
      setBusy(false)
    }
  }

  const onSubmit = (event: FormEvent) => { event.preventDefault(); void submit(false) }

  if (stores.isPending) return <Skeleton className="h-40" />
  if (stores.isError) return <ErrorState title="Não foi possível carregar as lojas." message={stores.error instanceof ApiError ? stores.error.message : undefined} onRetry={() => stores.refetch()} />
  if (active.length === 0) {
    return <Card title="Nenhuma loja com caixa ativo." text="Cadastre a loja e o caixa em Loja → Lojas e caixas, na retaguarda, e volte aqui." action={<button type="button" onClick={onCancel} className="min-h-14 rounded-md border border-border px-5 font-semibold">Voltar</button>} />
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">Registrar este aparelho</h2>
      <FormError message={error ?? undefined} />
      {replace ? (
        <div role="alert" className="space-y-3 rounded-md border border-warn bg-warn-soft p-4">
          <p className="font-semibold">{replace}</p>
          <p className="text-sm">O aparelho anterior é bloqueado e o caixa começa uma numeração nova de vendas, sem misturar com as do anterior.</p>
          <div className="flex flex-wrap gap-2">
            <button type="button" disabled={busy} onClick={() => { setReplace(null); void submit(true) }} className="min-h-14 rounded-md bg-primary px-5 font-semibold text-primary-foreground">Trocar o aparelho</button>
            <button type="button" onClick={() => setReplace(null)} className="min-h-14 rounded-md border border-border px-5 font-semibold">Escolher outro caixa</button>
          </div>
        </div>
      ) : null}
      <FormGrid>
        <Field label="Loja" required size="lg">
          {(p) => (
            <select {...p} value={store?.id ?? ''} onChange={(e) => { setStoreId(e.target.value); setRegisterId('') }} className={inputClass}>
              {active.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Caixa" required size="md">
          {(p) => (
            <select {...p} value={register?.id ?? ''} onChange={(e) => setRegisterId(e.target.value)} className={inputClass}>
              {registers.map((r) => <option key={r.id} value={r.id}>Caixa {registerLabel(r.number)}{r.device ? ` (já tem: ${r.device.name})` : ''}</option>)}
            </select>
          )}
        </Field>
        <Field label="Nome do aparelho" size="lg" hint="Como ele aparece na lista de aparelhos. Ex.: Caixa da frente.">
          {(p) => <input {...p} value={name} maxLength={40} placeholder={register ? `Caixa ${registerLabel(register.number)}` : ''} onChange={(e) => setName(e.target.value)} className={inputClass} />}
        </Field>
      </FormGrid>
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={busy} className="min-h-14 rounded-md bg-primary px-5 font-semibold text-primary-foreground">{busy ? 'Registrando…' : 'Registrar este aparelho'}</button>
        <button type="button" onClick={onCancel} className="min-h-14 rounded-md border border-border px-5 font-semibold">Voltar</button>
      </div>
    </form>
  )
}
