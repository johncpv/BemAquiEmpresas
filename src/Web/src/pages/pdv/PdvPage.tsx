import { useNavigate, useSearch } from '@tanstack/react-router'
import { type FormEvent, type ReactNode, useCallback, useEffect, useMemo, useState } from 'react'
import { posHandshake, registerPosDevice, useListStores } from '@/api/generated/bemaqui'
import type { PosHandshake } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ensureSession } from '@/auth/session-guard'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { createDeviceKey, deviceSupported, forgetDeviceKey, loadDeviceKey, signRequest } from '@/pdv/device-signing'
import { registerLabel } from '@/pages/store/stores'

const HANDSHAKE = '/api/v1/pos/sync/handshake'

type State =
  | { kind: 'checking' }
  | { kind: 'unsupported' }
  | { kind: 'unregistered' }
  | { kind: 'blocked'; message: string }
  | { kind: 'error'; message: string }
  | { kind: 'clock'; message: string; serverTime: string | null }
  | { kind: 'registered'; register: PosHandshake }

/** Aperto de mão assinado pela chave do aparelho (ADR 0048 §2): diz se este aparelho é um caixa e de qual loja. */
async function check(): Promise<State> {
  if (!deviceSupported()) return { kind: 'unsupported' }
  const keys = await loadDeviceKey()
  if (!keys) return { kind: 'unregistered' }
  try {
    return { kind: 'registered', register: await posHandshake({ headers: await signRequest(keys, 'POST', HANDSHAKE) }) }
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) {
      // Relógio do aparelho fora da tolerância (revisão do PR 227, B5): acertar o relógio, não registrar de novo.
      if (e.code === 'device_clock_skew') return { kind: 'clock', message: e.message, serverTime: typeof e.data.serverTime === 'string' ? e.data.serverTime : null }
      return e.code === 'device_revoked' ? { kind: 'blocked', message: e.message } : { kind: 'unregistered' }
    }

    return { kind: 'error', message: e instanceof ApiError ? e.message : 'Não foi possível falar com o servidor.' }
  }
}

/**
 * Caixa (/pdv, PDV-01): por enquanto, só o registro do aparelho. Aparelho não registrado: "Este aparelho ainda não é um caixa." com "Registrar
 * este aparelho" (pede login completo, pos.manage_devices e o código do autenticador). A tela de venda chega na PDV-02, sobre a OFF-01.
 */
export function PdvPage() {
  const search = useSearch({ strict: false }) as { registrar?: string }
  const navigate = useNavigate()
  const [state, setState] = useState<State>({ kind: 'checking' })
  const [registering, setRegistering] = useState(false)

  const startRegister = useCallback(async () => {
    if (await ensureSession()) {
      setRegistering(true)
      return
    }

    window.location.assign('/entrar?volta=' + encodeURIComponent('/pdv?registrar=1'))
  }, [])

  const refresh = useCallback(() => {
    setState({ kind: 'checking' })
    void check().then(setState)
  }, [])

  // Na abertura: confere o aparelho; voltando do login com ?registrar=1, abre o registro direto.
  const wantsRegister = search.registrar === '1'
  useEffect(() => {
    let alive = true
    void check().then(async (next) => {
      if (!alive) return
      setState(next)
      if (wantsRegister && (next.kind === 'unregistered' || next.kind === 'blocked')) {
        await navigate({ to: '/pdv', search: {}, replace: true })
        await startRegister()
      }
    })
    return () => { alive = false }
  }, [wantsRegister, navigate, startRegister])

  return (
    <main id="conteudo" data-surface="floor" className="mx-auto min-h-dvh max-w-2xl space-y-6 bg-background p-4 text-foreground sm:p-8">
      <header className="space-y-1">
        <p className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Caixa (PDV)</p>
        <h1 className="font-display text-2xl font-bold">{state.kind === 'registered' ? `Caixa ${registerLabel(state.register.registerNumber)} · ${state.register.storeName}` : 'Este aparelho'}</h1>
      </header>
      {registering ? (
        <RegisterForm onDone={() => { setRegistering(false); void refresh() }} onCancel={() => setRegistering(false)} />
      ) : state.kind === 'checking' ? <Skeleton className="h-40" /> : state.kind === 'unsupported' ? (
        <Card title="Este navegador não serve de caixa." text="Use Windows com Chrome ou Edge, ou Android com Chrome. iPhone e iPad ficam fora do caixa." />
      ) : state.kind === 'clock' ? (
        <Card title="Acerte o relógio deste aparelho."
          text={`A data e a hora deste aparelho estão diferentes das do servidor${state.serverTime ? ` (agora: ${new Date(state.serverTime).toLocaleString('pt-BR')})` : ''}. Acerte o relógio nas configurações do aparelho e confira de novo.`}
          action={<button type="button" onClick={() => void refresh()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Conferir de novo</button>} />
      ) : state.kind === 'error' ? (
        <ErrorState title="Não foi possível conferir o aparelho." message={state.message} onRetry={() => void refresh()} />
      ) : state.kind === 'registered' ? (
        <Card title={`Este aparelho é o caixa ${registerLabel(state.register.registerNumber)} da loja ${state.register.storeName}.`}
          text={`As vendas deste aparelho vão sair numeradas a partir de ${state.register.salePrefix}000001. A tela de venda chega na próxima versão.`} />
      ) : (
        <Card title={state.kind === 'blocked' ? 'Este aparelho foi bloqueado ou trocado por outro.' : 'Este aparelho ainda não é um caixa.'}
          text={state.kind === 'blocked' ? 'Para voltar a vender nele, registre o aparelho de novo.' : 'Registre o aparelho para ele vender por um caixa da loja. É preciso entrar com e-mail e senha de quem pode registrar aparelhos.'}
          action={<button type="button" onClick={() => void startRegister()} className="min-h-14 rounded-md bg-primary px-5 text-base font-semibold text-primary-foreground">Registrar este aparelho</button>} />
      )}
    </main>
  )
}

function Card({ title, text, action }: { title: string; text: string; action?: ReactNode }) {
  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-5">
      <h2 className="text-lg font-semibold">{title}</h2>
      <p className="text-muted-foreground">{text}</p>
      {action}
    </section>
  )
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
