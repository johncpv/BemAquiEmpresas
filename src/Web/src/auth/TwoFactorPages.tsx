import { Link, Navigate, useNavigate } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { beginRequiredTwoFactorSetup, confirmRequiredTwoFactorSetup, verifyTwoFactor } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { markEphemeralSession, setAccessToken } from '@/api/session'
import { inputClass } from '@/components/form/form-utils'
import { InlineError } from '@/components/ui/states'
import { AuthLayout } from './AuthLayout'
import { landingAfterLogin } from './home-route'
import { pendingChallenge } from './pending-challenge'
import { linkClass, primaryButton } from './styles'
import { TwoFactorSetup } from './TwoFactorSetup'

const ENDED = ['auth.2fa_challenge_expired', 'auth.2fa_too_many_attempts']

/** Desafio do login (FND-05 CA 4 a 7): código do app ou de recuperação; 5 erros derrubam o desafio. */
export function TwoFactorVerifyPage() {
  const navigate = useNavigate()
  const pending = pendingChallenge.get()
  const [recovery, setRecovery] = useState(false)
  const [value, setValue] = useState('')
  const [error, setError] = useState<{ message: string; ended: boolean }>()
  if (!pending || pending.purpose !== 'verify') return <Navigate to="/entrar" />

  const submit = async () => {
    setError(undefined)
    try {
      const result = await verifyTwoFactor({ challenge: pending.challenge, code: recovery ? null : value, recoveryCode: recovery ? value : null, ephemeral: pending.ephemeral ?? false })
      setAccessToken(result.accessToken)
      markEphemeralSession(pending.ephemeral ?? false)
      pendingChallenge.set(null)
      await navigate({ to: await landingAfterLogin(pending.volta) })
    } catch (e) {
      const api = e instanceof ApiError ? e : null
      setError({ message: api?.message ?? 'Não foi possível conferir o código.', ended: !!api?.code && ENDED.includes(api.code) })
    }
  }

  return (
    <AuthLayout title="Verificação em duas etapas">
      <form onSubmit={(e) => { e.preventDefault(); void submit() }} className="space-y-4">
        <div>
          <label htmlFor="codigo" className="block text-sm font-medium">{recovery ? 'Código de recuperação' : 'Código do app autenticador'}</label>
          <input
            id="codigo"
            autoFocus
            value={value}
            onChange={(e) => setValue(e.target.value)}
            inputMode={recovery ? 'text' : 'numeric'}
            autoComplete="one-time-code"
            aria-invalid={!!error}
            aria-describedby={error ? 'codigo-erro' : undefined}
            className={inputClass + ' mt-1 text-center font-mono tracking-[0.3em]'}
          />
          <InlineError id="codigo-erro" message={error?.message} />
        </div>
        {error?.ended ? (
          <Link to="/entrar" className={primaryButton + ' block text-center'}>Entrar de novo</Link>
        ) : (
          <button type="submit" className={primaryButton}>Confirmar</button>
        )}
        <button type="button" onClick={() => { setRecovery(!recovery); setValue(''); setError(undefined) }} className={linkClass + ' block w-full text-center'}>
          {recovery ? 'Usar o código do app' : 'Usar código de recuperação'}
        </button>
      </form>
    </AuthLayout>
  )
}

/** 2FA obrigatório e ainda não configurado (FND-05 CA 8): só libera a configuração, sem "Fazer depois". */
export function RequiredTwoFactorSetupPage() {
  const navigate = useNavigate()
  const pending = pendingChallenge.get()
  const api = useMemo(
    () =>
      pending && {
        begin: () => beginRequiredTwoFactorSetup({ challenge: pending.challenge }),
        confirm: (code: string) => confirmRequiredTwoFactorSetup({ challenge: pending.challenge, code, ephemeral: pending.ephemeral ?? false }),
      },
    [pending],
  )
  if (!pending || pending.purpose !== 'setup' || !api) return <Navigate to="/entrar" />

  return (
    <AuthLayout title="Configure a verificação em duas etapas">
      <p className="mb-4 text-sm text-muted-foreground">A empresa exige a verificação em duas etapas para a sua conta. Leva um minuto.</p>
      <TwoFactorSetup
        api={api}
        onActivated={(token) => { setAccessToken(token); markEphemeralSession(pending.ephemeral ?? false) }}
        onDone={() => {
          pendingChallenge.set(null)
          void landingAfterLogin(pending.volta).then((to) => navigate({ to }))
        }}
      />
    </AuthLayout>
  )
}
