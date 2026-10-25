import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { login } from '@/api/generated/bemaqui'
import { markEphemeralSession, setAccessToken } from '@/api/session'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { z } from '@/lib/zod'
import { landingAfterLogin } from './home-route'
import { AuthLayout } from './AuthLayout'
import { pendingChallenge, safeReturn } from './pending-challenge'
import { linkClass, primaryButton } from './styles'

const schema = z.object({ email: z.email('Informe um e-mail válido.'), password: z.string().min(1, 'Informe a senha.') })

/**
 * Entrar (FND-04): erro uniforme do servidor; com 2FA vai ao desafio ou à configuração (FND-05). Com <c>aparelho=1</c> (vindo da
 * entrada do tablet da fábrica), o login não deixa sessão persistente: sem refresh, só o token em memória (PRD-03b CA 6).
 */
export function LoginPage() {
  const search = useSearch({ strict: false }) as { volta?: string; email?: string; aparelho?: string }
  const ephemeral = search.aparelho === '1'
  const navigate = useNavigate()
  const form = useZodForm(schema, { defaultValues: { email: search.email ?? '', password: '' } })
  const [busy, setBusy] = useState(false)
  const volta = safeReturn(search.volta)

  const submit = form.handleSubmit(async (values) => {
    setBusy(true)
    try {
      const result = await login({ ...values, ephemeral })
      if (result.accessToken) {
        setAccessToken(result.accessToken)
        markEphemeralSession(ephemeral)
        await navigate({ to: await landingAfterLogin(volta) })
      } else if (result.twoFactor) {
        pendingChallenge.set({ ...result.twoFactor, purpose: result.twoFactor.purpose === 'setup' ? 'setup' : 'verify', volta, ephemeral })
        await navigate({ to: result.twoFactor.purpose === 'setup' ? '/entrar/configurar-2fa' : '/entrar/verificacao' })
      }
    } catch (error) {
      if (!applyServerErrors(form, error)) form.setError('root.server', { message: 'Não foi possível entrar agora. Tente de novo.' })
    } finally {
      setBusy(false)
    }
  })

  const { errors } = form.formState
  return (
    <AuthLayout title={ephemeral ? 'Entrar no tablet da fábrica' : 'Entrar'}>
      {ephemeral ? <p className="mb-4 text-sm text-muted-foreground">Esta entrada não fica salva no tablet: serve para registrar o aparelho ou criar o seu PIN.</p> : null}
      <form onSubmit={submit} noValidate className="space-y-4">
        <FormError message={errors.root?.server?.message} />
        <Field label="E-mail" error={errors.email?.message}>
          {(p) => <input {...p} type="email" autoComplete="username" autoFocus={!search.email} {...form.register('email')} className={inputClass} />}
        </Field>
        <Field label="Senha" error={errors.password?.message}>
          {(p) => <input {...p} type="password" autoComplete="current-password" autoFocus={!!search.email} {...form.register('password')} className={inputClass} />}
        </Field>
        <button type="submit" disabled={busy} className={primaryButton}>{busy ? 'Entrando…' : 'Entrar'}</button>
        <p className="text-center">
          <Link to="/esqueci-senha" search={{ email: form.watch('email') || undefined }} className={linkClass}>Esqueci minha senha</Link>
        </p>
      </form>
    </AuthLayout>
  )
}
