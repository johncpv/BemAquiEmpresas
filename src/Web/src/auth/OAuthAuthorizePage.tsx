import { Link } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { consentOAuthAuthorize, denyOAuthAuthorize, inspectOAuthAuthorize } from '@/api/generated/bemaqui'
import type { AuthorizeInspection } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { InlineError, Skeleton } from '@/components/ui/states'
import { authorizeErrorText, authorizeRequestFrom, browser } from '@/pages/settings/integrations/conecta'
import { AuthLayout } from './AuthLayout'
import { primaryButton } from './styles'

const INTENT_MESSAGE = 'Este pedido de conexão não foi iniciado por você neste ERP.'
const secondaryButton = 'w-full rounded-md border border-border bg-card px-3 py-2.5 text-sm font-semibold disabled:opacity-60'

type Screen =
  | { kind: 'loading' }
  | { kind: 'invalid'; message: string }
  | { kind: 'failed' }
  | { kind: 'leaving'; text: string }
  | { kind: 'consent'; inspection: AuthorizeInspection }

/** Sem Referer nesta tela (INT-01 CA 5): o código e o state nunca vão a outro site. */
function useNoReferrer() {
  useEffect(() => {
    const meta = document.createElement('meta')
    meta.name = 'referrer'
    meta.content = 'no-referrer'
    document.head.appendChild(meta)
    return () => meta.remove()
  }, [])
}

/**
 * Autorizar (INT-01 CA 3 a 10): tela dedicada, como a do login. O pedido do aplicativo vai ao ERP sem alteração; erro de cliente, endereço ou
 * PKCE fica aqui, sem redirecionar. A pessoa vê o que o Conecta poderá fazer, o que fica de fora e confirma com o código do autenticador.
 */
export function OAuthAuthorizePage() {
  useNoReferrer()
  const [request] = useState(() => authorizeRequestFrom(new URLSearchParams(browser.search())))
  const [screen, setScreen] = useState<Screen>({ kind: 'loading' })
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [enroll, setEnroll] = useState(false)
  const codeInput = useRef<HTMLInputElement>(null)

  const leave = (url: string, text: string) => {
    setScreen({ kind: 'leaving', text })
    browser.assign(url)
  }

  useEffect(() => {
    let alive = true
    inspectOAuthAuthorize(request)
      .then((inspection) => {
        if (!alive) return
        if (inspection.outcome === 'redirect' && inspection.redirectTo) leave(inspection.redirectTo, 'Voltando ao Conecta…')
        else if (inspection.outcome === 'consent') setScreen({ kind: 'consent', inspection })
        else setScreen({ kind: 'invalid', message: inspection.message ?? INTENT_MESSAGE })
      })
      .catch(() => alive && setScreen({ kind: 'failed' }))
    return () => {
      alive = false
    }
  }, [request])

  const authorize = async () => {
    setError(undefined)
    setEnroll(false)
    setBusy(true)
    try {
      const result = await consentOAuthAuthorize({ request, code })
      leave(result.redirectTo, 'Conectado. Voltando ao Conecta…')
    } catch (e) {
      const api = e instanceof ApiError ? e : null
      setBusy(false)
      if (api?.code === 'connect_intent_invalid' || api?.code === 'connect_intent_used') {
        setScreen({ kind: 'invalid', message: INTENT_MESSAGE })
      } else if (api?.code === 'two_factor_required') {
        setEnroll(true)
      } else {
        setError(authorizeErrorText(api?.code, api?.message))
        setCode('')
        codeInput.current?.focus()
      }
    }
  }

  const cancel = async () => {
    setBusy(true)
    try {
      const result = await denyOAuthAuthorize(request)
      leave(result.redirectTo, 'Voltando ao Conecta…')
    } catch {
      setBusy(false)
      setScreen({ kind: 'invalid', message: INTENT_MESSAGE })
    }
  }

  if (screen.kind === 'loading') {
    return <AuthLayout title="Autorizar o Conecta"><div aria-busy="true" className="space-y-2"><Skeleton className="h-6" /><Skeleton className="h-24" /></div></AuthLayout>
  }

  if (screen.kind === 'leaving') {
    return <AuthLayout title="Autorizar o Conecta"><p role="status" className="text-sm">{screen.text}</p></AuthLayout>
  }

  if (screen.kind === 'invalid' || screen.kind === 'failed') {
    return (
      <AuthLayout title="Não foi possível conectar">
        <p role="alert" className="text-sm">{screen.kind === 'failed' ? 'Não foi possível conferir o pedido agora. Tente de novo pelo ERP.' : screen.message}</p>
        <Link to="/app/configuracoes/integracoes/conecta" className={primaryButton + ' mt-4 block text-center'}>Ir para Integrações → Conecta</Link>
      </AuthLayout>
    )
  }

  const inspection = screen.inspection
  return (
    <AuthLayout title={'Autorizar o ' + (inspection.appName ?? 'Conecta')}>
      <div className="space-y-4 text-sm">
        <p>
          {inspection.accountLabel ? <>A conta <b>{inspection.accountLabel}</b> do {inspection.appName} </> : <>O {inspection.appName} </>}
          quer se conectar a <b>{inspection.tenantName ?? 'este ERP'}</b>.
        </p>
        <section aria-labelledby="pode">
          <h2 id="pode" className="font-semibold">O que o Conecta poderá fazer</h2>
          <ul className="mt-1 list-disc space-y-1 pl-5">{inspection.granted.map((s) => <li key={s.scope}>{s.text}</li>)}</ul>
        </section>
        {inspection.notHeld.length > 0 ? (
          <section aria-labelledby="nao-pode">
            <h2 id="nao-pode" className="font-semibold">O Conecta não vai receber, porque você não tem esse acesso</h2>
            <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">{inspection.notHeld.map((s) => <li key={s.scope}>{s.text}</li>)}</ul>
          </section>
        ) : null}
        <p className="text-muted-foreground">Você pode desconectar quando quiser, em Configurações → Integrações → Conecta.</p>
        {enroll ? (
          <div role="alert" className="space-y-2">
            <p>Para conectar, ative a verificação em duas etapas.</p>
            <a href={'/app/conta/seguranca?volta=' + encodeURIComponent(window.location.pathname + window.location.search)} className={primaryButton + ' block text-center'}>Ativar 2FA</a>
          </div>
        ) : (
          <form onSubmit={(e) => { e.preventDefault(); void authorize() }} className="space-y-3">
            <div>
              <label htmlFor="codigo" className="block font-medium">Código do aplicativo autenticador</label>
              <input
                id="codigo"
                ref={codeInput}
                autoFocus
                value={code}
                onChange={(e) => setCode(e.target.value)}
                inputMode="numeric"
                autoComplete="one-time-code"
                aria-invalid={!!error}
                aria-describedby={error ? 'codigo-erro' : undefined}
                className={inputClass + ' mt-1 text-center font-mono tracking-[0.3em]'}
              />
              <InlineError id="codigo-erro" message={error} />
            </div>
            <button type="submit" disabled={busy || code.trim().length === 0} className={primaryButton}>Autorizar</button>
            <button type="button" disabled={busy} onClick={() => void cancel()} className={secondaryButton}>Cancelar</button>
          </form>
        )}
      </div>
    </AuthLayout>
  )
}
